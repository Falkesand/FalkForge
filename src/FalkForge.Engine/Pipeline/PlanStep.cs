namespace FalkForge.Engine.Pipeline;

using System.Diagnostics;
using System.Runtime.InteropServices;
using FalkForge.Diagnostics;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;

/// <summary>
/// Plan phase step. Invokes <see cref="Planner.CreatePlan"/> using the detection
/// result stored in <see cref="PipelineContext.Detection"/>, the UI-supplied
/// <see cref="UiRequest.Plan"/> parameters, and an optional <see cref="VariableStore"/>.
/// Populates <see cref="PipelineContext.Plan"/> on success.
/// </summary>
/// <remarks>
/// Performs a host-architecture compatibility check before creating the plan so that
/// an x64 MSI on an x86 OS surfaces as <see cref="ErrorKind.ArchitectureMismatch"/>
/// at plan time rather than MSI error 1603 at apply time.
///
/// Allowed combinations (matches Windows Compatibility rules):
/// <list type="bullet">
///   <item>x64 on x64 — native</item>
///   <item>x86 on x64 — WoW64</item>
///   <item>x86 on Arm64 — x86 emulation</item>
///   <item>x64 on Arm64 — x64 emulation</item>
///   <item>Neutral on any — always allowed</item>
/// </list>
/// </remarks>
internal sealed class PlanStep : IPlanStep
{
    private readonly Planner _planner;
    private readonly IUiChannel _uiChannel;
    private readonly VariableStore? _variableStore;
    private readonly Architecture _hostArchitecture;

    public PlanStep(
        Planner planner,
        IUiChannel uiChannel,
        VariableStore? variableStore = null,
        Architecture? hostArchitecture = null)
    {
        _planner = planner;
        _uiChannel = uiChannel;
        _variableStore = variableStore;
        // Default to the current process OS architecture (production). Tests inject a fake.
        _hostArchitecture = hostArchitecture ?? RuntimeInformation.OSArchitecture;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(
        PipelineContext ctx, UiRequest.Plan request, CancellationToken ct)
    {
        var startTs = Stopwatch.GetTimestamp();
        try
        {
            return await ExecuteCoreAsync(ctx, request, ct);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
            EngineMeter.RecordPhaseTransition(EnginePhase.Planning, elapsedMs);
        }
    }

    private async Task<Result<Unit>> ExecuteCoreAsync(
        PipelineContext ctx, UiRequest.Plan request, CancellationToken ct)
    {
        await _uiChannel.SendAsync(
            new PipelineEvent.PhaseChanged(EnginePhase.Planning), ct);

        if (ctx.Manifest is null)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "PlanStep: manifest not populated — DetectStep must run first.");

        if (ctx.Detection is null)
            return Result<Unit>.Failure(ErrorKind.EngineError,
                "PlanStep: detection result not populated — DetectStep must run first.");

        // Architecture gate: reject packages whose required architecture cannot run on
        // the host OS. Checked here (plan time) so operators see a typed error before
        // the installation starts rather than a generic MSI 1603 failure mid-apply.
        var archCheck = CheckArchitectureCompatibility(ctx.Manifest.Packages, _hostArchitecture);
        if (archCheck.IsFailure)
            return archCheck;

        // License gate: when manifest requires a license, the UI must have accepted it.
        // Silent mode auto-accepts (headless/CLI installs). When the manifest has no
        // LicenseFile the gate is skipped entirely.
        if (ctx.Manifest.LicenseFile is not null)
        {
            if (!ctx.SilentMode && request.LicenseAccepted is not true)
            {
                return Result<Unit>.Failure(ErrorKind.EngineError,
                    "License agreement has not been accepted. " +
                    "Set LicenseAccepted = true in the plan request to proceed.");
            }

            if (ctx.SilentMode)
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Log(LogLevel.Info,
                        "Silent mode: license auto-accepted"),
                    ct);
            }
        }

        ctx.PlanRequest = request;

        // Propagate user properties into the variable store so that condition
        // evaluation and secret-bracket expansion work correctly during planning.
        if (_variableStore is not null)
        {
            foreach (var (key, value) in request.Properties)
                _variableStore.Set(key, value);
        }

        var secretNames = request.SecureProperties.Count > 0
            ? (IReadOnlySet<string>)new HashSet<string>(
                request.SecureProperties.Keys, StringComparer.OrdinalIgnoreCase)
            : null;

        var planResult = _planner.CreatePlan(
            manifest: ctx.Manifest,
            detection: ctx.Detection.Value,
            action: request.Action,
            variables: _variableStore,
            detectedRelatedBundles: ctx.RelatedBundles.Count > 0
                ? ctx.RelatedBundles
                : null,
            featureSelections: request.FeatureSelections.Count > 0
                ? request.FeatureSelections
                : null,
            userProperties: request.Properties.Count > 0
                ? request.Properties
                : null,
            secretPropertyNames: secretNames,
            packageFeatureSelections: request.PackageFeatureSelections is { Count: > 0 }
                ? request.PackageFeatureSelections
                : null);

        if (planResult.IsFailure)
            return Result<Unit>.Failure(planResult.Error);

        ctx.Plan = planResult.Value;

        // Per-package plan notifications, derived from the completed plan in chain order.
        // The planner computes the plan as a whole; these observational Begin/Complete pairs
        // report each package's planned action to the UI in order.
        foreach (var action in planResult.Value.Actions)
        {
            var plannedAction = action.ActionType.ToString();
            var displayName = action.Package.DisplayName ?? action.PackageId;
            await _uiChannel.SendAsync(
                new PipelineEvent.PlanPackageBegin(action.PackageId, displayName, plannedAction),
                ct);
            await _uiChannel.SendAsync(
                new PipelineEvent.PlanPackageComplete(action.PackageId, displayName, plannedAction),
                ct);
        }

        await _uiChannel.SendAsync(
            new PipelineEvent.Log(LogLevel.Info,
                $"Plan created: {planResult.Value.Actions.Count} action(s)"),
            ct);

        return Unit.Value;
    }

    /// <summary>
    /// Validates that every package's required architecture is compatible with
    /// <paramref name="hostArch"/>. Returns failure on the first incompatible package.
    /// </summary>
    private static Result<Unit> CheckArchitectureCompatibility(
        IEnumerable<PackageInfo> packages,
        Architecture hostArch)
    {
        foreach (var pkg in packages)
        {
            if (pkg.Architecture == PackageArchitecture.Neutral)
                continue; // no constraint — always allowed

            if (!IsCompatible(pkg.Architecture, hostArch))
            {
                var pkgArch = pkg.Architecture.ToString().ToLowerInvariant();
                var host = hostArch.ToString().ToLowerInvariant();
                return Result<Unit>.Failure(
                    ErrorKind.ArchitectureMismatch,
                    $"Package '{pkg.Id}' requires {pkgArch} but the host OS is {host}. " +
                    $"This package cannot be installed on this machine.");
            }
        }

        return Unit.Value;
    }

    /// <summary>
    /// Returns true when a package with the given <paramref name="pkgArch"/> can run
    /// on a host with <paramref name="hostArch"/>, following Windows compatibility rules.
    /// </summary>
    private static bool IsCompatible(PackageArchitecture pkgArch, Architecture hostArch) =>
        (pkgArch, hostArch) switch
        {
            // Native matches
            (PackageArchitecture.X86,   Architecture.X86)   => true,
            (PackageArchitecture.X64,   Architecture.X64)   => true,
            (PackageArchitecture.Arm64, Architecture.Arm64) => true,

            // WoW64: x86 runs on x64
            (PackageArchitecture.X86, Architecture.X64) => true,

            // Windows-on-Arm emulation: x86 and x64 run on Arm64
            (PackageArchitecture.X86, Architecture.Arm64) => true,
            (PackageArchitecture.X64, Architecture.Arm64) => true,

            // Everything else (x64 on x86, Arm64 on x64, etc.) is incompatible
            _ => false
        };
}
