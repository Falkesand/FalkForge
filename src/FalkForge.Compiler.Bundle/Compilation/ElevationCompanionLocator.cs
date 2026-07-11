using FalkForge.Engine.Protocol.Bundle;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Resolves the published NativeAOT <c>FalkForge.Engine.Elevation.exe</c> that a runnable bundle
/// embeds as its trust-covered elevation companion payload. The companion executes elevated
/// (SYSTEM for per-machine installs); without it a distributed bundle exe can never construct an
/// elevation gateway and silently degrades to per-user-only. A runnable bundle therefore carries
/// the companion by default and FAILS LOUD when none can be found — opting out is explicit
/// (<see cref="BundleModel.OmitElevationCompanion"/>), never a silent fallback.
/// <para>Resolution order (mirrors <see cref="EngineStubLocator"/>'s trustworthy sources —
/// fresh publish output, never a committed binary):</para>
/// <list type="number">
///   <item><description>The compiler's explicit <c>ElevationCompanionPath</c> — when set it wins
///   unconditionally and MUST exist (a configured-but-missing companion is a build error).</description></item>
///   <item><description>The design-time placeholder opt-in: a placeholder bundle embeds no engine,
///   so it embeds no companion either (hermetic — ambient state is never consulted).</description></item>
///   <item><description>The <c>FALKFORGE_ELEVATION_COMPANION</c> environment variable — a path to
///   the companion executable or a directory containing it. When set it is authoritative.</description></item>
///   <item><description>Beside the engine being embedded: the explicit <c>EngineStubPath</c>'s
///   directory, or the directory of the engine found by default resolution
///   (<see cref="EngineStubLocator.Resolve()"/>). <c>scripts/publish.ps1</c> publishes the engine
///   and the companion into the same output directory, so a real publish always resolves.</description></item>
/// </list>
/// </summary>
internal static class ElevationCompanionLocator
{
    public const string EnvironmentVariableName = "FALKFORGE_ELEVATION_COMPANION";

    /// <summary>
    /// Resolves the companion executable to embed, honoring the shared stub/companion policy.
    /// Returns <see cref="ElevationCompanionResolution.None"/> when the bundle legitimately
    /// carries no companion (explicit opt-out or design-time placeholder), the resolved path when
    /// found, and a loud <see cref="ErrorKind.BundleError"/> failure when a runnable bundle's
    /// companion is missing.
    /// </summary>
    internal static Result<ElevationCompanionResolution> Resolve(
        string? explicitCompanionPath,
        string? explicitStubPath,
        bool allowPlaceholderStub,
        bool omitCompanion,
        Func<Result<string>> engineResolver)
        => Resolve(explicitCompanionPath, explicitStubPath, allowPlaceholderStub, omitCompanion,
            engineResolver, Environment.GetEnvironmentVariable(EnvironmentVariableName));

    /// <summary>
    /// Testable core of <see cref="Resolve(string?, string?, bool, bool, Func{Result{string}})"/>
    /// with the environment value injected.
    /// </summary>
    internal static Result<ElevationCompanionResolution> Resolve(
        string? explicitCompanionPath,
        string? explicitStubPath,
        bool allowPlaceholderStub,
        bool omitCompanion,
        Func<Result<string>> engineResolver,
        string? environmentValue)
    {
        // Explicit opt-out: a bundle authored per-user-only need not carry the companion.
        if (omitCompanion)
            return Result<ElevationCompanionResolution>.Success(ElevationCompanionResolution.None);

        // Explicit path wins unconditionally (same policy as EngineStubPath): the operator asked
        // for a specific companion and must get it or an error — never a silent substitute.
        if (explicitCompanionPath is not null)
        {
            if (!File.Exists(explicitCompanionPath))
                return Result<ElevationCompanionResolution>.Failure(ErrorKind.BundleError,
                    $"Elevation companion not found at the configured ElevationCompanionPath: {explicitCompanionPath}");

            return Validate(Path.GetFullPath(explicitCompanionPath));
        }

        // Design-time placeholder: no engine is embedded, so no companion is embedded. Hermetic —
        // ambient resolution (environment variable, publish output) is never consulted.
        if (allowPlaceholderStub)
            return Result<ElevationCompanionResolution>.Success(ElevationCompanionResolution.None);

        // Environment override is authoritative when set: an unresolvable value is a
        // configuration error, not a reason to probe elsewhere.
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return ResolveFromEnvironmentValue(environmentValue);

        // Default: the companion lives beside the engine being embedded (scripts/publish.ps1
        // publishes both executables into the same directory).
        string engineDirectorySource;
        if (explicitStubPath is not null)
        {
            // Companion resolution runs before the stub is materialized; a configured-but-missing
            // stub must surface as the STUB error (same message as EngineStubLocator.CreateStubFile),
            // not as a misleading missing-companion error beside a path that never existed.
            if (!File.Exists(explicitStubPath))
                return Result<ElevationCompanionResolution>.Failure(ErrorKind.BundleError,
                    $"Engine stub not found at the configured EngineStubPath: {explicitStubPath}");

            engineDirectorySource = explicitStubPath;
        }
        else
        {
            var engine = engineResolver();
            if (engine.IsFailure)
                return Result<ElevationCompanionResolution>.Failure(engine.Error);
            engineDirectorySource = engine.Value;
        }

        var engineDirectory = Path.GetDirectoryName(Path.GetFullPath(engineDirectorySource));
        var candidate = engineDirectory is null
            ? null
            : Path.Combine(engineDirectory, EngineCompanionPayload.PackageId);

        if (candidate is not null && File.Exists(candidate))
            return Validate(candidate);

        return Result<ElevationCompanionResolution>.Failure(ErrorKind.BundleError,
            $"No published {EngineCompanionPayload.PackageId} could be located beside the engine " +
            $"being embedded ({engineDirectorySource}). A runnable bundle carries the elevation " +
            "companion so per-machine (elevated) installs work from a lone distributed exe; " +
            "without it elevation is impossible at install time. Run scripts/publish.ps1 (which " +
            "publishes the engine and the companion together), set the " +
            $"{EnvironmentVariableName} environment variable or the compiler's " +
            "ElevationCompanionPath to the published companion, or set " +
            "OmitElevationCompanion=true on the bundle model " +
            "(BundleBuilder.WithoutElevationCompanion()) to deliberately build a per-user-only bundle.");
    }

    private static Result<ElevationCompanionResolution> ResolveFromEnvironmentValue(string value)
    {
        if (File.Exists(value))
            return Validate(Path.GetFullPath(value));

        if (Directory.Exists(value))
        {
            var candidate = Path.Combine(value, EngineCompanionPayload.PackageId);
            if (File.Exists(candidate))
                return Validate(Path.GetFullPath(candidate));

            return Result<ElevationCompanionResolution>.Failure(ErrorKind.BundleError,
                $"{EnvironmentVariableName} points to a directory that does not contain " +
                $"{EngineCompanionPayload.PackageId}: {value}");
        }

        return Result<ElevationCompanionResolution>.Failure(ErrorKind.BundleError,
            $"{EnvironmentVariableName} is set but does not point to an existing file or " +
            $"directory: {value}");
    }

    /// <summary>
    /// Sanity-checks that a resolved companion is a Windows executable (MZ header) and is
    /// self-contained (no sibling <c>FalkForge.Engine.Elevation.dll</c> apphost pair). This guards
    /// against embedding a truncated, corrupt, or framework-dependent artifact; it is not a trust
    /// check — the runtime trust binding is the manifest hash + signature envelope.
    /// </summary>
    private static Result<ElevationCompanionResolution> Validate(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && File.Exists(Path.Combine(directory, "FalkForge.Engine.Elevation.dll")))
        {
            return Result<ElevationCompanionResolution>.Failure(ErrorKind.BundleError,
                $"Resolved elevation companion is a framework-dependent apphost " +
                $"(FalkForge.Engine.Elevation.dll found beside it), which cannot run once " +
                $"extracted alone at install time: {path}. Publish the self-contained NativeAOT " +
                "companion instead (scripts/publish.ps1).");
        }

        Span<byte> header = stackalloc byte[2];
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < header.Length || stream.Read(header) != header.Length ||
                header[0] != (byte)'M' || header[1] != (byte)'Z')
            {
                return Result<ElevationCompanionResolution>.Failure(ErrorKind.BundleError,
                    $"Resolved elevation companion is not a valid Windows executable (missing MZ header): {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ElevationCompanionResolution>.Failure(ErrorKind.BundleError,
                $"Resolved elevation companion could not be read: {path} ({ex.Message})");
        }

        return Result<ElevationCompanionResolution>.Success(new ElevationCompanionResolution(path));
    }
}
