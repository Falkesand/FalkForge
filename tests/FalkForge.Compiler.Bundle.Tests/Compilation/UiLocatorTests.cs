using FalkForge.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Verifies the UI-binary resolution order that lets a runnable bundle find the published
/// framework-dependent single-file <c>FalkForge.Ui.exe</c> to embed: explicit <c>UiPath</c> first
/// (must exist), then the design-time placeholder opt-in (hermetic — no UI embedded, no ambient
/// state consulted), then the <c>FALKFORGE_UI</c> environment variable (authoritative when set),
/// then a location beside the RAW <c>FALKFORGE_ENGINE_STUB</c> value (never beside
/// <see cref="EngineStubLocator"/>'s COMPUTED result — a later change may relocate that result, see
/// <see cref="Resolve_EngineStubEnvValue_ResolvesEvenWhenIndependentProbingWouldFail"/>), then
/// independent probing mirroring <see cref="EngineStubLocator"/>'s own candidate list.
/// </summary>
public sealed class UiLocatorTests : IDisposable
{
    private readonly string _tempDir;

    public UiLocatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UiLocator_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // Cleanup is best-effort; a locked handle or transient I/O error must not fail the test.
        TestTemp.TryDelete(_tempDir);
    }

    /// <summary>Writes a minimal fake PE (MZ header + padding) usable as a stand-in UI binary.</summary>
    private static string WriteFakeUi(string directory, string fileName = "FalkForge.Ui.exe")
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ── explicit UiPath wins ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_ExplicitUiPath_WinsOverEverythingElse()
    {
        var explicitUi = WriteFakeUi(Path.Combine(_tempDir, "explicit"));
        // Everything below would also resolve to a (different) UI if UiPath did not win outright.
        var probedDir = Path.Combine(_tempDir, "probed");
        var probedUi = WriteFakeUi(probedDir);

        var result = UiLocator.Resolve(
            explicitUiPath: explicitUi,
            allowPlaceholderStub: false,
            uiEnvironmentValue: probedUi,
            engineStubEnvironmentValue: probedUi,
            baseDirectory: probedDir,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(explicitUi, result.Value);
        Assert.NotEqual(probedUi, result.Value);
    }

    [Fact]
    public void Resolve_ExplicitUiPathMissing_FailsLoud()
    {
        var missing = Path.Combine(_tempDir, "no-such-ui.exe");

        var result = UiLocator.Resolve(
            explicitUiPath: missing,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: null,
            baseDirectory: null,
            currentDirectory: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("UiPath", result.Error.Message, StringComparison.Ordinal);
    }

    // ── placeholder opt-in ────────────────────────────────────────────────────

    [Fact]
    public void Resolve_AllowPlaceholderStub_ResolvesToNoUiWithoutFailing()
    {
        // Hermetic: ambient state (env vars, probing) is never consulted, so passing non-null
        // values here that WOULD resolve proves they are ignored rather than merely absent.
        var ignoredDir = Path.Combine(_tempDir, "ignored");
        WriteFakeUi(ignoredDir);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: true,
            uiEnvironmentValue: Path.Combine(ignoredDir, "FalkForge.Ui.exe"),
            engineStubEnvironmentValue: ignoredDir,
            baseDirectory: ignoredDir,
            currentDirectory: ignoredDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value);
    }

    // ── FALKFORGE_UI environment variable ────────────────────────────────────

    [Fact]
    public void Resolve_Parameterless_ReadsRealFalkforgeUiEnvironmentVariable()
    {
        var ui = WriteFakeUi(_tempDir);
        Environment.SetEnvironmentVariable(UiLocator.EnvironmentVariableName, ui);
        try
        {
            var result = UiLocator.Resolve(explicitUiPath: null, allowPlaceholderStub: false);

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            Assert.Equal(ui, result.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UiLocator.EnvironmentVariableName, null);
        }
    }

    [Fact]
    public void Resolve_UiEnvironmentValuePointsToDirectory_ResolvesUiExeInside()
    {
        var uiDir = Path.Combine(_tempDir, "uidir");
        var ui = WriteFakeUi(uiDir);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: uiDir,
            engineStubEnvironmentValue: null,
            baseDirectory: null,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    [Fact]
    public void Resolve_UiEnvironmentValueSetButTargetMissing_FailsLoud_NeverFallsThrough()
    {
        // An explicitly configured location that does not resolve is a configuration error;
        // silently probing elsewhere could embed a different UI than the operator intended.
        var missing = Path.Combine(_tempDir, "no-such-ui.exe");
        var fallbackBase = Path.Combine(_tempDir, "base");
        WriteFakeUi(fallbackBase);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: missing,
            engineStubEnvironmentValue: null,
            baseDirectory: fallbackBase,
            currentDirectory: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains(UiLocator.EnvironmentVariableName, result.Error.Message, StringComparison.Ordinal);
    }

    // ── FALKFORGE_ENGINE_STUB-relative resolution ────────────────────────────

    [Fact]
    public void Resolve_EngineStubEnvironmentValuePointsToFile_ResolvesUiBesideIt()
    {
        var engineDir = Path.Combine(_tempDir, "engine");
        var enginePath = WriteFakeUi(engineDir, "FalkForge.Engine.exe");
        var ui = WriteFakeUi(engineDir);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: enginePath,
            baseDirectory: null,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    [Fact]
    public void Resolve_EngineStubEnvironmentValuePointsToDirectory_ResolvesUiInside()
    {
        var engineDir = Path.Combine(_tempDir, "engine2");
        var ui = WriteFakeUi(engineDir);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: engineDir,
            baseDirectory: null,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    [Fact]
    public void Resolve_EngineStubEnvValue_ResolvesEvenWhenIndependentProbingWouldFail()
    {
        // Proves resolution chains off the RAW FALKFORGE_ENGINE_STUB value, never off
        // EngineStubLocator.Resolve()'s COMPUTED result: baseDirectory/currentDirectory here have
        // no engine and no UI at all, so if this method re-derived the engine location through
        // independent probing instead of trusting the raw env value directly, it would fail here.
        // The method signature only accepts the raw string -- never a resolver callback -- so
        // there is no computed result available for it to chain off in the first place.
        var engineDir = Path.Combine(_tempDir, "declared-engine-location");
        var ui = WriteFakeUi(engineDir);
        var unrelatedEmptyDir = Path.Combine(_tempDir, "unrelated-empty");
        Directory.CreateDirectory(unrelatedEmptyDir);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: engineDir,
            baseDirectory: unrelatedEmptyDir,
            currentDirectory: unrelatedEmptyDir);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    [Fact]
    public void Resolve_EngineStubEnvValueHasNoUiBesideIt_FallsThroughToProbing()
    {
        // The FALKFORGE_ENGINE_STUB candidate is a convenience default, not an operator-declared
        // UI location -- an unresolved candidate here must fall through to independent probing
        // rather than failing loud (unlike the FALKFORGE_UI variable itself, which is
        // authoritative when set).
        var engineDirWithNoUi = Path.Combine(_tempDir, "engine-no-ui");
        Directory.CreateDirectory(engineDirWithNoUi);
        var baseDir = Path.Combine(_tempDir, "app");
        var ui = WriteFakeUi(baseDir);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: engineDirWithNoUi,
            baseDirectory: baseDir,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    // ── independent probing (normal route) ───────────────────────────────────

    [Fact]
    public void Resolve_UiBesideHostApplication_IsFound()
    {
        var baseDir = Path.Combine(_tempDir, "app2");
        var ui = WriteFakeUi(baseDir);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: null,
            baseDirectory: baseDir,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    [Fact]
    public void Resolve_UiSubdirectoryOfHostApplication_IsFound()
    {
        var baseDir = Path.Combine(_tempDir, "app3");
        Directory.CreateDirectory(baseDir);
        var ui = WriteFakeUi(Path.Combine(baseDir, "engine"));

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: null,
            baseDirectory: baseDir,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    [Fact]
    public void Resolve_UiDirectorySiblingOfHostApplication_IsFound()
    {
        // publish.ps1 layout: <Output>/forge (CLI) next to <Output>/engine (engine + companion +
        // UI binaries, all published into the same directory).
        var publishRoot = Path.Combine(_tempDir, "publish");
        var forgeDir = Path.Combine(publishRoot, "forge");
        Directory.CreateDirectory(forgeDir);
        var ui = WriteFakeUi(Path.Combine(publishRoot, "engine"));

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: null,
            baseDirectory: forgeDir,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    [Fact]
    public void Resolve_RepoWalkUpFromBaseDirectory_FindsPublishedUi()
    {
        // Dev/repo flow: running from <repo>/src/X/bin/Release/net10.0 must find the UI
        // published by scripts/publish.ps1 at <repo>/artifacts/publish/engine (same directory
        // as the engine and companion).
        var repoRoot = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(repoRoot);
        File.WriteAllText(Path.Combine(repoRoot, "FalkForge.slnx"), "<Solution/>");
        var ui = WriteFakeUi(Path.Combine(repoRoot, "artifacts", "publish", "engine"));

        var deepBin = Path.Combine(repoRoot, "src", "App", "bin", "Release", "net10.0");
        Directory.CreateDirectory(deepBin);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: null,
            baseDirectory: deepBin,
            currentDirectory: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ui, result.Value);
    }

    // ── validation: MZ header ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_UiPathPointsToNonPeFile_Fails()
    {
        var bogus = Path.Combine(_tempDir, "FalkForge.Ui.exe");
        File.WriteAllText(bogus, "this is not a PE file but is long enough to pass a size floor check");

        var result = UiLocator.Resolve(
            explicitUiPath: bogus,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: null,
            baseDirectory: null,
            currentDirectory: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("MZ", result.Error.Message, StringComparison.Ordinal);
    }

    // ── validation: sibling DLL (single-file publish guard) ──────────────────

    [Fact]
    public void Resolve_UiPathHasSiblingDll_Fails()
    {
        // The single-file UI publish bundles its managed assembly INTO the exe and produces no
        // sibling FalkForge.Ui.dll (measured across round 1-3's reviews). A sibling dll means
        // someone published without PublishSingleFile -- the resulting apphost cannot run once
        // extracted alone at install time, so this must be rejected rather than embedded.
        var dir = Path.Combine(_tempDir, "fdd");
        var ui = WriteFakeUi(dir);
        File.WriteAllBytes(Path.Combine(dir, "FalkForge.Ui.dll"), [0x4D, 0x5A]);

        var result = UiLocator.Resolve(
            explicitUiPath: ui,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: null,
            baseDirectory: null,
            currentDirectory: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("FalkForge.Ui.dll", result.Error.Message, StringComparison.Ordinal);
    }

    // ── failure is actionable ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_NothingFound_FailsWithActionableMessage()
    {
        var emptyBase = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyBase);

        var result = UiLocator.Resolve(
            explicitUiPath: null,
            allowPlaceholderStub: false,
            uiEnvironmentValue: null,
            engineStubEnvironmentValue: null,
            baseDirectory: emptyBase,
            currentDirectory: emptyBase);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        // The message must tell the operator every way out: publish the UI, point the
        // environment variable at it, or deliberately opt into a non-runnable placeholder.
        Assert.Contains("publish.ps1", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(UiLocator.EnvironmentVariableName, result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("AllowPlaceholderStub", result.Error.Message, StringComparison.Ordinal);
    }
}
