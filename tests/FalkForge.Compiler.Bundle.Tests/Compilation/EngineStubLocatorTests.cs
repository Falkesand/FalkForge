using FalkForge.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Verifies the engine-stub resolution order that makes a default bundle build embed the REAL
/// published NativeAOT engine: explicit environment variable first (file or directory), then
/// well-known locations next to the host application, then the repository publish output found
/// by walking up to the FalkForge.slnx marker. Resolution must fail loud — with an actionable
/// message — rather than silently fall back to a non-runnable placeholder, because the embedded
/// engine is what performs the install AND the runtime trust verification.
/// </summary>
public sealed class EngineStubLocatorTests : IDisposable
{
    private readonly string _tempDir;

    public EngineStubLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"EngineStubLocator_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Writes a minimal fake PE (MZ header + padding) usable as a stand-in engine binary.</summary>
    private string WriteFakeEngine(string directory, string fileName = "FalkForge.Engine.exe")
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ── environment variable ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_EnvVarPointsToFile_ReturnsThatFile()
    {
        var engine = WriteFakeEngine(_tempDir);

        var result = EngineStubLocator.Resolve(engine, baseDirectory: null, currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(engine, result.Value);
    }

    [Fact]
    public void Resolve_EnvVarPointsToDirectory_ResolvesEngineExeInside()
    {
        var engine = WriteFakeEngine(Path.Combine(_tempDir, "engine"));

        var result = EngineStubLocator.Resolve(
            Path.Combine(_tempDir, "engine"), baseDirectory: null, currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(engine, result.Value);
    }

    [Fact]
    public void Resolve_EnvVarSetButTargetMissing_FailsLoud_NeverFallsThrough()
    {
        // An explicitly configured location that does not resolve is a configuration error;
        // silently probing elsewhere could embed a different engine than the operator intended.
        var missing = Path.Combine(_tempDir, "no-such-engine.exe");
        var fallbackBase = Path.Combine(_tempDir, "base");
        WriteFakeEngine(fallbackBase);

        var result = EngineStubLocator.Resolve(missing, fallbackBase, currentDirectory: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains(EngineStubLocator.EnvironmentVariableName, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_EnvVarPointsToNonPeFile_Fails()
    {
        var bogus = Path.Combine(_tempDir, "FalkForge.Engine.exe");
        File.WriteAllText(bogus, "this is not a PE file but is long enough to pass a size floor check");

        var result = EngineStubLocator.Resolve(bogus, baseDirectory: null, currentDirectory: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("MZ", result.Error.Message, StringComparison.Ordinal);
    }

    // ── host application directory probes ────────────────────────────────────

    [Fact]
    public void Resolve_EngineBesideHostApplication_IsFound()
    {
        var baseDir = Path.Combine(_tempDir, "app");
        var engine = WriteFakeEngine(baseDir);

        var result = EngineStubLocator.Resolve(null, baseDir, currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(engine, result.Value);
    }

    [Fact]
    public void Resolve_EngineSubdirectoryOfHostApplication_IsFound()
    {
        var baseDir = Path.Combine(_tempDir, "app");
        Directory.CreateDirectory(baseDir);
        var engine = WriteFakeEngine(Path.Combine(baseDir, "engine"));

        var result = EngineStubLocator.Resolve(null, baseDir, currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(engine, result.Value);
    }

    [Fact]
    public void Resolve_EngineDirectorySiblingOfHostApplication_IsFound()
    {
        // publish.ps1 layout: <Output>/forge (CLI) next to <Output>/engine (NativeAOT binaries).
        var publishRoot = Path.Combine(_tempDir, "publish");
        var forgeDir = Path.Combine(publishRoot, "forge");
        Directory.CreateDirectory(forgeDir);
        var engine = WriteFakeEngine(Path.Combine(publishRoot, "engine"));

        var result = EngineStubLocator.Resolve(null, forgeDir, currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(engine, result.Value);
    }

    // ── framework-dependent apphost rejection ────────────────────────────────
    //
    // A framework-dependent build output contains FalkForge.Engine.exe as a tiny apphost that
    // loads FalkForge.Engine.dll from BESIDE ITSELF. Embedding that apphost as the bundle front
    // produces an exe that cannot start (the dll is not next to the shipped bundle) — the exact
    // "verifies but does not install" failure this feature removes. Only a self-contained
    // (NativeAOT) engine, recognizable by the ABSENCE of a sibling FalkForge.Engine.dll, is a
    // valid stub.

    [Fact]
    public void Resolve_ApphostBesideHost_IsSkipped_ResolutionContinues()
    {
        var baseDir = Path.Combine(_tempDir, "bin");
        WriteFakeEngine(baseDir); // apphost…
        File.WriteAllBytes(Path.Combine(baseDir, "FalkForge.Engine.dll"), [0x4D, 0x5A]); // …with sibling dll
        var realEngine = WriteFakeEngine(Path.Combine(baseDir, "engine")); // self-contained candidate

        var result = EngineStubLocator.Resolve(null, baseDir, currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(realEngine, result.Value);
    }

    [Fact]
    public void Resolve_EnvVarPointsToApphost_FailsLoud()
    {
        var dir = Path.Combine(_tempDir, "fdd");
        var apphost = WriteFakeEngine(dir);
        File.WriteAllBytes(Path.Combine(dir, "FalkForge.Engine.dll"), [0x4D, 0x5A]);

        var result = EngineStubLocator.Resolve(apphost, baseDirectory: null, currentDirectory: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("self-contained", result.Error.Message, StringComparison.Ordinal);
    }

    // ── repository walk-up probe ─────────────────────────────────────────────

    [Fact]
    public void Resolve_RepoWalkUpFromBaseDirectory_FindsPublishedEngine()
    {
        // Dev/repo flow: running from <repo>/src/X/bin/Release/net10.0 must find the engine
        // published by scripts/publish.ps1 at <repo>/artifacts/publish/engine.
        var repoRoot = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoRoot);
        File.WriteAllText(Path.Combine(repoRoot, "FalkForge.slnx"), "<Solution/>");
        var engine = WriteFakeEngine(Path.Combine(repoRoot, "artifacts", "publish", "engine"));

        var deepBin = Path.Combine(repoRoot, "src", "App", "bin", "Release", "net10.0");
        Directory.CreateDirectory(deepBin);

        var result = EngineStubLocator.Resolve(null, deepBin, currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(engine, result.Value);
    }

    [Fact]
    public void Resolve_RepoWalkUpFromCurrentDirectory_FindsPublishedEngine()
    {
        // Packaged `forge` runs from ~/.dotnet/tools; the repo is only reachable via the
        // working directory the user invoked it from.
        var repoRoot = Path.Combine(_tempDir, "repo2");
        Directory.CreateDirectory(repoRoot);
        File.WriteAllText(Path.Combine(repoRoot, "FalkForge.slnx"), "<Solution/>");
        var engine = WriteFakeEngine(Path.Combine(repoRoot, "artifacts", "publish", "engine"));

        var toolDir = Path.Combine(_tempDir, "tools");
        Directory.CreateDirectory(toolDir);
        var workingDir = Path.Combine(repoRoot, "demo", "35-bundle-simple");
        Directory.CreateDirectory(workingDir);

        var result = EngineStubLocator.Resolve(null, toolDir, workingDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(engine, result.Value);
    }

    // ── failure is actionable ────────────────────────────────────────────────

    [Fact]
    public void Resolve_NothingFound_FailsWithActionableMessage()
    {
        var emptyBase = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyBase);

        var result = EngineStubLocator.Resolve(null, emptyBase, emptyBase);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        // The message must tell the operator every way out: publish the engine, point the
        // environment variable at it, or deliberately opt into a non-runnable placeholder.
        Assert.Contains("publish.ps1", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(EngineStubLocator.EnvironmentVariableName, result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("AllowPlaceholderStub", result.Error.Message, StringComparison.Ordinal);
    }
}
