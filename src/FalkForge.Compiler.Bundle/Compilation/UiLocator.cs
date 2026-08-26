using FalkForge.Configuration;
using FalkForge.Engine.Protocol.Bundle;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Resolves the published framework-dependent single-file <c>FalkForge.Ui.exe</c> that a runnable
/// bundle embeds as its trust-covered UI payload. Without it a distributed bundle exe can extract
/// and verify itself but has nothing to launch — "No UI executable found in bundle payloads" — so
/// resolution fails loud when none can be found, the same policy <see cref="EngineStubLocator"/>
/// and <see cref="ElevationCompanionLocator"/> already use.
/// <para>Resolution order:</para>
/// <list type="number">
///   <item><description>The compiler's explicit <c>UiPath</c> — when set it wins unconditionally
///   and MUST exist (a configured-but-missing UI is a build error).</description></item>
///   <item><description>The design-time placeholder opt-in: a placeholder bundle embeds no
///   engine, so it embeds no UI either (hermetic — ambient resolution is never
///   consulted).</description></item>
///   <item><description>The <c>FALKFORGE_UI</c> environment variable — a path to the UI
///   executable or a directory containing it. When set it is authoritative: an unresolvable
///   value is a configuration error, not a reason to probe elsewhere.</description></item>
///   <item><description>Beside a user-declared engine location: the compiler's explicit
///   <c>EngineStubPath</c> first, then the RAW <c>FALKFORGE_ENGINE_STUB</c> value. Both are
///   places a person named, so both are honoured — pointing the compiler at a published engine
///   directory used to hand back the elevation companion (which resolves from exactly the same
///   place) and then fail on the UI. What resolution must NEVER chain off is
///   <see cref="EngineStubLocator.Resolve()"/>'s COMPUTED result: that is a resolved outcome a
///   later change to the engine-stub resolver could relocate, and chaining off it would silently
///   break UI resolution. Neither candidate here is authoritative the way <c>FALKFORGE_UI</c> is:
///   the person declared where the ENGINE lives, not where the UI lives, so an unresolved
///   candidate falls through to independent probing instead of failing loud. The environment
///   variable is also the only path that resolves under the documented
///   <c>FalkForgeCopyEngineToOutput=false</c> on the SDK route.</description></item>
///   <item><description>Independent probing, mirroring <see cref="EngineStubLocator"/>'s own
///   candidate list: beside the host application (<see cref="AppContext.BaseDirectory"/>), an
///   <c>engine</c> subdirectory, a sibling <c>engine</c> directory, then the repository publish
///   output found by walking up to a <c>FalkForge.slnx</c> marker (<c>scripts/publish.ps1</c>
///   publishes the UI into the same <c>artifacts/publish/engine</c> directory as the engine and
///   the elevation companion).</description></item>
/// </list>
/// </summary>
internal static class UiLocator
{
    public const string EnvironmentVariableName = EnvVarCatalog.Ui;

    private const string RepoMarkerFileName = "FalkForge.slnx";

    /// <summary>
    /// Resolves the UI executable to embed from the process environment: the compiler's explicit
    /// seams plus <c>FALKFORGE_UI</c>, <c>FALKFORGE_ENGINE_STUB</c>, the host application
    /// directory, and the enclosing repository's publish output, in that order.
    /// </summary>
    public static Result<string?> Resolve(
        string? explicitUiPath, bool allowPlaceholderStub, string? explicitEngineStubPath = null)
        => Resolve(
            explicitUiPath,
            allowPlaceholderStub,
            EnvVarCatalog.GetRaw(EnvironmentVariableName),
            explicitEngineStubPath,
            EnvVarCatalog.GetRaw(EngineStubLocator.EnvironmentVariableName),
            AppContext.BaseDirectory,
            Environment.CurrentDirectory);

    /// <summary>
    /// Testable core of <see cref="Resolve(string?, bool, string?)"/> with every ambient input
    /// injected. Both engine-location parameters are values a person supplied verbatim —
    /// <paramref name="explicitEngineStubPath"/> is the compiler's <c>EngineStubPath</c> as set,
    /// <paramref name="engineStubEnvironmentValue"/> is the RAW <c>FALKFORGE_ENGINE_STUB</c>
    /// string. Neither is a resolved/computed engine path, so this method structurally cannot
    /// chain off <see cref="EngineStubLocator"/>'s computed resolution (there is no resolver
    /// callback parameter for it to invoke).
    /// </summary>
    internal static Result<string?> Resolve(
        string? explicitUiPath,
        bool allowPlaceholderStub,
        string? uiEnvironmentValue,
        string? explicitEngineStubPath,
        string? engineStubEnvironmentValue,
        string? baseDirectory,
        string? currentDirectory)
    {
        // Explicit path wins unconditionally (same policy as EngineStubPath/ElevationCompanionPath):
        // the operator asked for a specific UI binary and must get it or an error — never a silent
        // substitute.
        if (explicitUiPath is not null)
        {
            if (!File.Exists(explicitUiPath))
                return Result<string?>.Failure(ErrorKind.BundleError,
                    $"UI executable not found at the configured UiPath: {explicitUiPath}");

            return Validate(Path.GetFullPath(explicitUiPath));
        }

        // Design-time placeholder: no engine is embedded, so no UI is embedded either. Hermetic —
        // ambient resolution (environment variables, probing) is never consulted.
        if (allowPlaceholderStub)
            return Result<string?>.Success(null);

        // FALKFORGE_UI is authoritative when set: an unresolvable value is a configuration error,
        // not a reason to probe elsewhere.
        if (!string.IsNullOrWhiteSpace(uiEnvironmentValue))
            return ResolveFromEnvironmentValue(uiEnvironmentValue);

        // Beside a user-declared engine location: the compiler's explicit EngineStubPath, then the
        // RAW FALKFORGE_ENGINE_STUB value. Neither is authoritative — an unresolved candidate here
        // falls through to independent probing rather than failing loud, because what the operator
        // declared is where the ENGINE is, not where the UI is.
        var besideEngineStub = TryResolveBesideDeclaredEngineLocation(explicitEngineStubPath)
                               ?? TryResolveBesideDeclaredEngineLocation(engineStubEnvironmentValue);
        if (besideEngineStub is not null)
            return Validate(besideEngineStub);

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            var probes = new[]
            {
                Path.Combine(baseDirectory, UiPayload.PackageId),
                Path.Combine(baseDirectory, "engine", UiPayload.PackageId),
                Path.Combine(baseDirectory, "..", "engine", UiPayload.PackageId)
            };

            foreach (var probe in probes)
            {
                if (File.Exists(probe))
                    return Validate(Path.GetFullPath(probe));
            }
        }

        var repoUi = ProbeRepositoryPublishOutput(baseDirectory) ?? ProbeRepositoryPublishOutput(currentDirectory);
        if (repoUi is not null)
            return Validate(repoUi);

        return Result<string?>.Failure(ErrorKind.BundleError,
            $"No published {UiPayload.PackageId} could be located. A runnable bundle carries the " +
            "UI so the extracted, verified bundle has something to launch; without it the install " +
            "cannot proceed. Run scripts/publish.ps1 (or `dotnet publish src/FalkForge.Ui -c " +
            "Release -r win-x64 --self-contained false -p:PublishSingleFile=true`), set the " +
            $"{EnvironmentVariableName} environment variable to the published UI, or set " +
            "AllowPlaceholderStub=true to deliberately build a non-runnable design-time bundle.");
    }

    private static Result<string?> ResolveFromEnvironmentValue(string value)
    {
        if (File.Exists(value))
            return Validate(Path.GetFullPath(value));

        if (Directory.Exists(value))
        {
            var candidate = Path.Combine(value, UiPayload.PackageId);
            if (File.Exists(candidate))
                return Validate(Path.GetFullPath(candidate));

            return Result<string?>.Failure(ErrorKind.BundleError,
                $"{EnvironmentVariableName} points to a directory that does not contain " +
                $"{UiPayload.PackageId}: {value}");
        }

        return Result<string?>.Failure(ErrorKind.BundleError,
            $"{EnvironmentVariableName} is set but does not point to an existing file or " +
            $"directory: {value}");
    }

    /// <summary>
    /// Looks for the UI beside an engine location a person declared verbatim — the compiler's
    /// <c>EngineStubPath</c> or the RAW <c>FALKFORGE_ENGINE_STUB</c> value. Never a computed
    /// engine path. Never fails: the declaration names where the engine is, not where the UI is,
    /// so an unset value, an unresolvable one, or a directory without the UI beside it all return
    /// null and resolution falls through to independent probing.
    /// </summary>
    private static string? TryResolveBesideDeclaredEngineLocation(string? declaredEngineLocation)
    {
        if (string.IsNullOrWhiteSpace(declaredEngineLocation))
            return null;

        string? directory;
        if (File.Exists(declaredEngineLocation))
            directory = Path.GetDirectoryName(Path.GetFullPath(declaredEngineLocation));
        else if (Directory.Exists(declaredEngineLocation))
            directory = Path.GetFullPath(declaredEngineLocation);
        else
            return null;

        if (directory is null)
            return null;

        var candidate = Path.Combine(directory, UiPayload.PackageId);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> to a directory containing the
    /// <c>FalkForge.slnx</c> repository marker and probes its <c>artifacts/publish/engine</c>
    /// output — the same directory <c>scripts/publish.ps1</c> publishes the UI into, beside the
    /// engine and the elevation companion. Returns null when no marker or no published UI is found.
    /// </summary>
    private static string? ProbeRepositoryPublishOutput(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return null;

        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, RepoMarkerFileName)))
            {
                var candidate = Path.Combine(
                    dir.FullName, "artifacts", "publish", "engine", UiPayload.PackageId);
                return File.Exists(candidate) ? candidate : null;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Sanity-checks that a resolved UI candidate is a Windows executable (MZ header) and is a
    /// genuine single-file publish (no sibling <c>FalkForge.Ui.dll</c> apphost pair). A UI
    /// published without <c>PublishSingleFile</c> produces exactly that shape — an apphost that
    /// cannot run once extracted alone at install time — so this guards against embedding a
    /// misconfigured publish, not a trust check (the runtime trust binding is the manifest hash +
    /// signature envelope, wired in a later landing).
    /// <para>Deliberately NOT shared with <see cref="ElevationCompanionLocator"/>'s equivalent
    /// private, companion-typed <c>Validate</c> (which hardcodes
    /// <c>FalkForge.Engine.Elevation.dll</c> and returns <c>ElevationCompanionResolution</c>):
    /// generalising that method would couple two payload types whose messages, sibling-dll name,
    /// and result shape may need to evolve independently, for one narrow guard reused nowhere
    /// else. A small UI-specific copy is safer here than a shared abstraction with one
    /// caller.</para>
    /// </summary>
    private static Result<string?> Validate(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && File.Exists(Path.Combine(directory, "FalkForge.Ui.dll")))
        {
            return Result<string?>.Failure(ErrorKind.BundleError,
                $"Resolved UI executable is a framework-dependent apphost (FalkForge.Ui.dll " +
                $"found beside it), which cannot run once extracted alone at install time: {path}. " +
                "Publish the single-file UI instead (scripts/publish.ps1, or `dotnet publish " +
                "src/FalkForge.Ui -c Release -r win-x64 --self-contained false " +
                "-p:PublishSingleFile=true`).");
        }

        Span<byte> header = stackalloc byte[2];
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < header.Length || stream.Read(header) != header.Length ||
                header[0] != (byte)'M' || header[1] != (byte)'Z')
            {
                return Result<string?>.Failure(ErrorKind.BundleError,
                    $"Resolved UI executable is not a valid Windows executable (missing MZ header): {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string?>.Failure(ErrorKind.BundleError,
                $"Resolved UI executable could not be read: {path} ({ex.Message})");
        }

        return Result<string?>.Success(path);
    }
}
