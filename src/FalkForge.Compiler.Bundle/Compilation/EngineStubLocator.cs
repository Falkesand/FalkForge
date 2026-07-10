namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Resolves the published NativeAOT <c>FalkForge.Engine.exe</c> that becomes the PE front of a
/// self-extracting bundle. The embedded engine is what performs the install AND the runtime
/// trust verification, so bundle compilation embeds a REAL engine by default and fails loud when
/// none can be found — the design-time placeholder is an explicit opt-in
/// (<see cref="BundleCompiler.AllowPlaceholderStub"/>), never a silent fallback.
/// <para>Resolution order:</para>
/// <list type="number">
///   <item><description>The <c>FALKFORGE_ENGINE_STUB</c> environment variable — a path to the
///   engine executable or to a directory containing it. When set it is authoritative: an
///   unresolvable value is a configuration error, not a reason to probe elsewhere.</description></item>
///   <item><description>Well-known locations relative to the host application
///   (<see cref="AppContext.BaseDirectory"/>): the engine beside the host, an <c>engine</c>
///   subdirectory, or a sibling <c>engine</c> directory (the <c>scripts/publish.ps1</c> layout,
///   where the published <c>forge</c> CLI sits next to the published engine).</description></item>
///   <item><description>The repository publish output: walk up from the host application and the
///   working directory to a <c>FalkForge.slnx</c> marker, then probe
///   <c>artifacts/publish/engine/FalkForge.Engine.exe</c> (the dev/repo flow).</description></item>
/// </list>
/// </summary>
public static class EngineStubLocator
{
    public const string EnvironmentVariableName = "FALKFORGE_ENGINE_STUB";
    public const string EngineExecutableFileName = "FalkForge.Engine.exe";

    private const string RepoMarkerFileName = "FalkForge.slnx";

    /// <summary>
    /// Resolves the engine executable from the process environment: the
    /// <c>FALKFORGE_ENGINE_STUB</c> environment variable, the host application directory, and the
    /// enclosing repository's publish output, in that order.
    /// </summary>
    public static Result<string> Resolve() => Resolve(
        Environment.GetEnvironmentVariable(EnvironmentVariableName),
        AppContext.BaseDirectory,
        Environment.CurrentDirectory);

    /// <summary>
    /// Testable core of <see cref="Resolve()"/> with every ambient input injected.
    /// </summary>
    internal static Result<string> Resolve(
        string? environmentValue, string? baseDirectory, string? currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return ResolveFromEnvironmentValue(environmentValue);

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            var probes = new[]
            {
                Path.Combine(baseDirectory, EngineExecutableFileName),
                Path.Combine(baseDirectory, "engine", EngineExecutableFileName),
                Path.Combine(baseDirectory, "..", "engine", EngineExecutableFileName)
            };

            foreach (var probe in probes)
            {
                // Skip (rather than fail on) framework-dependent apphosts here: ordinary build
                // output directories legitimately contain one, and a later probe may still find
                // the real self-contained engine.
                if (File.Exists(probe) && IsSelfContained(probe))
                    return Validate(Path.GetFullPath(probe));
            }
        }

        var repoEngine = ProbeRepositoryPublishOutput(baseDirectory)
            ?? ProbeRepositoryPublishOutput(currentDirectory);
        if (repoEngine is not null)
            return Validate(repoEngine);

        return Result<string>.Failure(ErrorKind.BundleError,
            "No published FalkForge engine could be located. Bundles embed the NativeAOT " +
            $"{EngineExecutableFileName} as their self-extracting front; without it the output " +
            "cannot install anything. Run scripts/publish.ps1 (or " +
            "`dotnet publish src/FalkForge.Engine -c Release -r win-x64`), set the " +
            $"{EnvironmentVariableName} environment variable to the published engine, or set " +
            "AllowPlaceholderStub=true to deliberately build a non-runnable design-time bundle.");
    }

    private static Result<string> ResolveFromEnvironmentValue(string value)
    {
        if (File.Exists(value))
            return Validate(Path.GetFullPath(value));

        if (Directory.Exists(value))
        {
            var candidate = Path.Combine(value, EngineExecutableFileName);
            if (File.Exists(candidate))
                return Validate(Path.GetFullPath(candidate));

            return Result<string>.Failure(ErrorKind.BundleError,
                $"{EnvironmentVariableName} points to a directory that does not contain " +
                $"{EngineExecutableFileName}: {value}");
        }

        return Result<string>.Failure(ErrorKind.BundleError,
            $"{EnvironmentVariableName} is set but does not point to an existing file or " +
            $"directory: {value}");
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> to a directory containing the
    /// <c>FalkForge.slnx</c> repository marker and probes its <c>artifacts/publish/engine</c>
    /// output. Returns null when no marker or no published engine is found.
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
                    dir.FullName, "artifacts", "publish", "engine", EngineExecutableFileName);
                return File.Exists(candidate) ? candidate : null;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// A framework-dependent build output ships <c>FalkForge.Engine.exe</c> as a tiny apphost
    /// that loads <c>FalkForge.Engine.dll</c> from beside itself — embedding that apphost as the
    /// bundle front yields an exe that cannot start once shipped alone. Only a self-contained
    /// (NativeAOT) engine — no sibling <c>FalkForge.Engine.dll</c> — is a usable stub.
    /// </summary>
    private static bool IsSelfContained(string enginePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(enginePath));
        return directory is null || !File.Exists(Path.Combine(directory, "FalkForge.Engine.dll"));
    }

    /// <summary>
    /// Sanity-checks that a resolved candidate is a Windows executable (MZ header) and is
    /// self-contained. This guards against embedding a truncated, corrupt, or
    /// framework-dependent artifact as the bundle front; it is not a trust check — Authenticode
    /// signing of the stub is a separate concern.
    /// </summary>
    private static Result<string> Validate(string path)
    {
        if (!IsSelfContained(path))
        {
            return Result<string>.Failure(ErrorKind.BundleError,
                $"Resolved engine stub is a framework-dependent apphost (FalkForge.Engine.dll " +
                $"found beside it), which cannot run as a standalone bundle front: {path}. " +
                "Publish the self-contained NativeAOT engine instead " +
                "(scripts/publish.ps1 or `dotnet publish src/FalkForge.Engine -c Release -r win-x64`).");
        }

        Span<byte> header = stackalloc byte[2];
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < header.Length || stream.Read(header) != header.Length ||
                header[0] != (byte)'M' || header[1] != (byte)'Z')
            {
                return Result<string>.Failure(ErrorKind.BundleError,
                    $"Resolved engine stub is not a valid Windows executable (missing MZ header): {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(ErrorKind.BundleError,
                $"Resolved engine stub could not be read: {path} ({ex.Message})");
        }

        return path;
    }

    /// <summary>
    /// Materializes the stub file a compiler prepends to the bundle output, honoring the shared
    /// stub policy: an explicit <paramref name="explicitStubPath"/> wins (and MUST exist — a
    /// configured-but-missing stub fails loud instead of silently degrading), then the explicit
    /// placeholder opt-in (hermetic: ambient resolution is never consulted), then default
    /// resolution via <paramref name="resolver"/>.
    /// </summary>
    internal static Result<string> CreateStubFile(
        string outputDir, string? explicitStubPath, bool allowPlaceholderStub, Func<Result<string>> resolver)
    {
        Directory.CreateDirectory(outputDir);
        var stubPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");

        if (explicitStubPath is not null)
        {
            if (!File.Exists(explicitStubPath))
                return Result<string>.Failure(ErrorKind.BundleError,
                    $"Engine stub not found at the configured EngineStubPath: {explicitStubPath}");

            File.Copy(explicitStubPath, stubPath, overwrite: true);
            return stubPath;
        }

        if (allowPlaceholderStub)
        {
            // Design-time placeholder (explicit opt-in): the bundle begins directly with the
            // FALKBUNDLE magic and is NOT a runnable installer.
            File.WriteAllBytes(stubPath, []);
            return stubPath;
        }

        var resolved = resolver();
        if (resolved.IsFailure)
            return Result<string>.Failure(resolved.Error);

        File.Copy(resolved.Value, stubPath, overwrite: true);
        return stubPath;
    }
}
