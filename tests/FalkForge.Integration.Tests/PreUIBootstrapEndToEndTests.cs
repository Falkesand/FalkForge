using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Bundle.Prerequisites;
using FalkForge.Engine;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// End-to-end integration test for the pre-UI prerequisite bootstrap pipeline.
///
/// Row 25 of the Phase 4 TDD spec (plan §10):
/// Mas_DemoBundle_PrereqMissing_TriggersPreUIInstall
///
/// Intent: Verify that a bundle authored with <c>.PreUIPrerequisite(...)</c> round-trips
/// through the compile pipeline, produces a manifest with the expected PreUIPackages entry,
/// and that <see cref="PreUIBootstrapOrchestrator"/> (with injected fakes) triggers the
/// install path when the detector reports the prereq missing.
///
/// Note: This is a regression-coverage test — all wiring was shipped in Phases 1–4 rows 1–24.
/// The test also serves as a forcing function for the <c>BuiltInPrerequisites.DotNet10DesktopAsPreUI()</c>
/// helper introduced by the MAS demo migration (plan §3 / §7).
/// </summary>
public sealed class PreUIBootstrapEndToEndTests
{
    // ── Helper types — mirrors fakes used in PreUIBootstrapOrchestratorTests ────

    private sealed class RecordingDetector : IPreUIPrerequisiteDetector
    {
        private readonly bool _reportMissing;
        public int CallCount { get; private set; }
        public IReadOnlyList<PreUIPackageInfo>? LastDeclared { get; private set; }

        public RecordingDetector(bool reportMissing) => _reportMissing = reportMissing;

        public List<PreUIPackageInfo> FindMissing(IReadOnlyList<PreUIPackageInfo> declared)
        {
            CallCount++;
            LastDeclared = declared;
            return _reportMissing ? [.. declared] : [];
        }
    }

    private sealed class RecordingInstaller : IPreUIPrerequisiteInstaller
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<PreUIPackageInfo>? LastMissing { get; private set; }

        public Task<PreUIResult> RunAllAsync(
            IReadOnlyList<PreUIPackageInfo> missing,
            IProgressSink progress,
            CancellationToken ct)
        {
            CallCount++;
            LastMissing = missing;
            return Task.FromResult<PreUIResult>(new PreUIResult.Success());
        }
    }

    private sealed class FakeElevationProbe : IElevationProbe
    {
        private readonly bool _elevated;
        public FakeElevationProbe(bool elevated) => _elevated = elevated;
        public bool IsElevated() => _elevated;
    }

    private sealed class FakeRelauncher : IElevatedSelfRelauncher
    {
        public int CallCount { get; private set; }
        public int Relaunch(string executablePath, string cacheDir, IReadOnlyList<string>? forwarded = null)
        {
            CallCount++;
            return 0;
        }
    }

    private sealed class NullProgressSinkFactory : IProgressSinkFactory
    {
        public IProgressSinkHandle Create() => new NullSink();

        private sealed class NullSink : IProgressSinkHandle
        {
            public void SetMessage(string text) { }
            public void SetPercent(int percent) { }
            public void Dispose() { }
        }
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mas_DemoBundle_PrereqMissing_TriggersPreUIInstall (row 25).
    ///
    /// Full pipeline:
    ///   1. Author a bundle with one PreUIPrerequisite (using BuiltInPrerequisites.DotNet10DesktopAsPreUI).
    ///   2. Compile it with BundleCompiler.
    ///   3. Extract manifest via PayloadEmbedder.Extract.
    ///   4. Verify manifest.PreUIPackages contains exactly one entry with the expected Id.
    ///   5. Run PreUIBootstrapOrchestrator with a fake detector reporting the prereq missing
    ///      and a fake installer that records the call.
    ///   6. Assert: outcome is LaunchUi (elevated in-process path) AND installer was invoked
    ///      with the missing package.
    /// </summary>
    [Fact]
    public async Task Mas_DemoBundle_PrereqMissing_TriggersPreUIInstall()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-e2e-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // ── Step 1: create a stub payload file (remote-only prereq has no embedded file,
            // but the compiler requires SourcePath to be empty string for remote payloads).
            // Use a tiny dummy file as a stand-in embedded payload so the bundle compiles
            // without needing a real .NET runtime installer.
            var stubPayload = Path.Combine(tempDir, "windowsdesktop-stub.exe");
            File.WriteAllBytes(stubPayload, RandomNumberGenerator.GetBytes(64));

            // ── Step 2: build bundle model with one PreUIPrerequisite ────────
            // Uses BuiltInPrerequisites.DotNet10DesktopAsPreUI() — this is the new helper
            // added by the MAS demo migration (plan §3 / §7). If the helper doesn't exist
            // this test won't compile, giving us the RED we need.
            var (preUISourcePath, preUIConfigure) = BuiltInPrerequisites.DotNet10DesktopAsPreUI(stubPayload);

            var model = new BundleBuilder()
                .Name("E2ETestBundle")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .PreUIPrerequisite(preUISourcePath, preUIConfigure)
                .Build();

            // ── Step 3: compile ───────────────────────────────────────────────
            var outputDir = Path.Combine(tempDir, "output");
            var compiler = new BundleCompiler();
            var compileResult = compiler.Compile(model, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Bundle compilation failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            // ── Step 4: extract and verify manifest has PreUIPackages ─────────
            var extractResult = PayloadEmbedder.Extract(compileResult.Value);
            Assert.True(extractResult.IsSuccess,
                $"Extraction failed: {(extractResult.IsFailure ? extractResult.Error.Message : "")}");

            var bundleContent = extractResult.Value;
            Assert.NotNull(bundleContent.ManifestJsonBytes);
            Assert.True(bundleContent.ManifestJsonBytes!.Length > 0, "Manifest JSON bytes are empty");

            // Deserialise using plain JsonSerializer — test projects are not AOT-compiled,
            // so reflection-based deserialisation is fine here.
            var manifest = JsonSerializer.Deserialize<InstallerManifest>(bundleContent.ManifestJsonBytes);
            Assert.NotNull(manifest);
            Assert.Single(manifest!.PreUIPackages);

            var prereqInfo = manifest.PreUIPackages[0];
            Assert.Equal("DotNet10Desktop", prereqInfo.Id);
            Assert.False(string.IsNullOrWhiteSpace(prereqInfo.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(prereqInfo.Arguments));
            Assert.NotEmpty(prereqInfo.SearchConditions);

            // ── Step 5: run orchestrator with fake detector reporting missing ──
            var detector   = new RecordingDetector(reportMissing: true);
            var installer  = new RecordingInstaller();
            var probe      = new FakeElevationProbe(elevated: true);   // already elevated → install in-process
            var relauncher = new FakeRelauncher();

            var orchestrator = new PreUIBootstrapOrchestrator(
                detector,
                installer,
                probe,
                relauncher,
                new NullProgressSinkFactory());

            var outcome = await orchestrator.RunAsync(
                manifest: manifest,
                args: BootstrapperArgs.Default,
                extractionDir: tempDir,
                ownExecutablePath: Path.Combine(tempDir, "setup.exe"),
                ct: CancellationToken.None);

            // ── Step 6: assertions ────────────────────────────────────────────
            // Elevated in-process path: installer ran → LaunchUi (not ExitSuccess).
            Assert.Equal(PreUIBootstrapOutcome.LaunchUi, outcome);
            Assert.Equal(1, detector.CallCount);
            Assert.Equal(1, installer.CallCount);
            Assert.Equal(0, relauncher.CallCount);   // no relaunch needed (already elevated)

            // The installer received exactly the DotNet10Desktop package.
            Assert.NotNull(installer.LastMissing);
            Assert.Single(installer.LastMissing);
            Assert.Equal("DotNet10Desktop", installer.LastMissing[0].Id);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// Regression: a bundle without any PreUIPrerequisite entries returns LaunchUi immediately
    /// without touching the detector or installer.
    /// </summary>
    [Fact]
    public async Task BuildBundle_Without_PreUIPrerequisite_ReturnsLaunchUi_Directly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-e2e-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var msiPath = Path.Combine(tempDir, "dummy.msi");
            File.WriteAllBytes(msiPath, RandomNumberGenerator.GetBytes(128));

            var model = new BundleBuilder()
                .Name("NoPreUIBundle")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Chain(chain => chain
                    .MsiPackage(msiPath, pkg => pkg
                        .Id("DummyMsi")
                        .DisplayName("Dummy MSI")))
                .Build();

            var outputDir = Path.Combine(tempDir, "output");
            var compiler = new BundleCompiler();
            var compileResult = compiler.Compile(model, outputDir);
            Assert.True(compileResult.IsSuccess);

            var extractResult = PayloadEmbedder.Extract(compileResult.Value);
            Assert.True(extractResult.IsSuccess);

            var bundleContent2 = extractResult.Value;
            Assert.NotNull(bundleContent2.ManifestJsonBytes);
            var manifest2 = JsonSerializer.Deserialize<InstallerManifest>(bundleContent2.ManifestJsonBytes!);
            Assert.NotNull(manifest2);
            Assert.Empty(manifest2!.PreUIPackages);   // no pre-UI prereqs

            // Orchestrator must short-circuit without calling detector/installer.
            var detector   = new RecordingDetector(reportMissing: false);
            var installer  = new RecordingInstaller();
            var probe      = new FakeElevationProbe(elevated: false);
            var relauncher = new FakeRelauncher();

            var orchestrator = new PreUIBootstrapOrchestrator(
                detector,
                installer,
                probe,
                relauncher,
                new NullProgressSinkFactory());

            var outcome = await orchestrator.RunAsync(
                manifest: manifest2,
                args: BootstrapperArgs.Default,
                extractionDir: tempDir,
                ownExecutablePath: Path.Combine(tempDir, "setup.exe"),
                ct: CancellationToken.None);

            Assert.Equal(PreUIBootstrapOutcome.LaunchUi, outcome);
            Assert.Equal(0, detector.CallCount);
            Assert.Equal(0, installer.CallCount);
            Assert.Equal(0, relauncher.CallCount);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup
        }
    }
}
