using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Characterization tests for the early-return FAILURE branches of <see cref="BootstrapperRunner.RunAsync"/>,
/// extracted (pure move) out of <c>Program.Main</c>'s self-extraction bootstrap path. <c>RunAsync</c>
/// hardcodes <c>Environment.ProcessPath!</c> as the bundle to self-extract, and a unit-test host process is
/// never itself a self-extracting bundle, so these tests use the same test seam already established by
/// <see cref="SelfExtractionMode"/>: an optional <c>exePathOverride</c> parameter that defaults to the
/// production <c>Environment.ProcessPath!</c> behavior when omitted.
/// <para>
/// Three of these stop at early-return failure branches (corrupt embedded manifest, the signed-payload
/// trust gate, and no UI executable found). The fourth goes further and drives the real UI launch and
/// handshake (<see cref="UiProcessLauncher.TryStartUiProcess"/> / <see cref="EngineSession.RunUntilShutdown"/>)
/// using a stand-in UI that exits at once, which is what a machine with no .NET Desktop Runtime produces.
/// The happy path — a UI that connects and drives an install — still needs a real published UI and stays in
/// the opt-in (<c>FALKFORGE_E2E</c>) end-to-end suite.
/// </para>
/// </summary>
public sealed class BootstrapperRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public BootstrapperRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BootstrapperRunnerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // The trust-gate test above registers/freezes the process-global EngineTrustAnchor via a real
        // signed bundle. Reset it here (same pattern as HybridBundleFluentEndToEndTests) so a future
        // test in this assembly that registers its own trusted keys is not broken by ours having run
        // first — this class currently gets away without it only because no other test in this
        // assembly both runs after this one and depends on a pristine anchor.
        EngineTrustAnchor.ResetForTests();

        if (Directory.Exists(_tempDir))
        {
            TestTemp.TryDelete(_tempDir);
        }
    }

    /// <summary>
    /// Builds a plain, unsigned bundle with a single MSI package and no elevation companion — the
    /// simplest possible valid bundle, reused by tests that need the "no UI executable" branch (an
    /// MSI-only chain never produces a payload that looks like a UI .exe).
    /// </summary>
    private string BuildUnsignedMsiOnlyBundle(string packageId)
    {
        var payloadPath = Path.Combine(_tempDir, $"{packageId}.msi");
        File.WriteAllBytes(payloadPath, RandomNumberGenerator.GetBytes(256));

        var model = new BundleBuilder()
            .Name("BootstrapperRunnerTest")
            .Manufacturer("Integration Tests")
            .Version("1.0.0")
            .UseSilentUI()
            .Chain(chain => chain.MsiPackage(payloadPath, pkg => pkg.Id(packageId).Version("1.0.0")))
            .Build();

        var buildResult = new BundleCompiler { AllowPlaceholderStub = true }
            .Compile(model, Path.Combine(_tempDir, $"out-{packageId}"));
        Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);
        return buildResult.Value;
    }

    private static (int ExitCode, string StdErr) RunCapturingStdErr(Func<Task<int>> run)
    {
        var originalErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var exit = run().GetAwaiter().GetResult();
            return (exit, sw.ToString());
        }
        finally
        {
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void RunAsync_CorruptEmbeddedManifest_ReturnsNonZeroExitCode()
    {
        // WHY: a hand-edited or partially-applied-transform bundle can carry a manifest region that
        // fails to deserialize. RunAsync must fail loud with a clear message here, not throw an
        // unhandled exception mid-bootstrap (which would surface as a crash dialog, not a clean error).
        var bundlePath = BuildUnsignedMsiOnlyBundle("CorruptManifestMsi");
        CorruptEmbeddedManifestBytes(bundlePath);

        var (exitCode, stdErr) = RunCapturingStdErr(
            () => BootstrapperRunner.RunAsync(exePathOverride: bundlePath));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("manifest", stdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunAsync_TrustGateFailure_TamperedSignedBundle_ReturnsInt006()
    {
        // WHY: this is the real security property the surrounding comments in BootstrapperRunner call
        // out — trust binding runs BEFORE any payload is extracted or the UI is launched (guaranteed by
        // code ordering: BundleTrustGate.Verify at line ~108 runs and returns before the payload-extraction
        // loop at line ~118 ever starts, so a failure here structurally cannot have extracted anything —
        // this test does not re-assert that ordering, only the trust-gate outcome). A validly signed
        // bundle whose payload bytes were rewritten after signing (so the overlay TOC hash no longer
        // matches the ECDSA-signed hash) must abort with the specific TOC-binding code INT006 — not just
        // any "integrity" failure, which would also match unrelated codes (INT004/INT012/INT001/INT008).
        // Same tamper technique as ForgeExtractTrustTests (the CLI's equivalent gate).
        var originalBytes = RandomNumberGenerator.GetBytes(256);
        var msiPath = Path.Combine(_tempDir, "TrustMsi.msi");
        File.WriteAllBytes(msiPath, originalBytes);

        var model = new BundleBuilder()
            .Name("TrustGateTest")
            .Manufacturer("Integration Tests")
            .Version("1.0.0")
            .UseSilentUI()
            .Integrity(i => { })
            .Chain(chain => chain.MsiPackage(msiPath, pkg => pkg.Id("TrustMsi").Version("1.0.0")))
            .Build();

        var buildResult = new BundleCompiler { AllowPlaceholderStub = true }
            .Compile(model, Path.Combine(_tempDir, "out-trust"));
        Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);

        var signedContent = PayloadEmbedder.Extract(buildResult.Value);
        Assert.True(signedContent.IsSuccess, signedContent.IsFailure ? signedContent.Error.Message : null);
        var signedManifest = System.Text.Json.JsonSerializer.Deserialize<InstallerManifest>(signedContent.Value.ManifestJsonBytes!);
        Assert.NotNull(signedManifest);

        // Attacker: tamper the payload bytes and re-embed with the UNCHANGED signed manifest but a TOC
        // hash matching the tampered bytes (a post-signing overlay rewrite).
        var tamperedBytes = (byte[])originalBytes.Clone();
        tamperedBytes[0] ^= 0xFF;
        var tamperedMsi = Path.Combine(_tempDir, "TrustMsi.tampered.msi");
        File.WriteAllBytes(tamperedMsi, tamperedBytes);
        var tamperedHash = Convert.ToHexString(SHA256.HashData(tamperedBytes));

        var stubPath = Path.Combine(_tempDir, "stub.bin");
        File.WriteAllBytes(stubPath, []);
        var attackerBundle = Path.Combine(_tempDir, "attacker.exe");

        var tamperedPayload = new PayloadEntry
        {
            PackageId = "TrustMsi",
            SourcePath = tamperedMsi,
            OriginalSize = tamperedBytes.Length,
            Sha256Hash = tamperedHash
        };

        var embed = new PayloadEmbedder().Embed(stubPath, attackerBundle, signedManifest, new[] { tamperedPayload });
        Assert.True(embed.IsSuccess, embed.IsFailure ? embed.Error.Message : null);

        var (exitCode, stdErr) = RunCapturingStdErr(
            () => BootstrapperRunner.RunAsync(exePathOverride: attackerBundle));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("INT006", stdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void RunAsync_BundleCarriesNoUiPayload_FailsLoudNamingTheRealCause()
    {
        // WHY: this bundle is a design-time placeholder build, so the compiler embedded no
        // FalkForge.Ui.exe payload and the manifest declares no UI hash. There is nothing to
        // launch, and the message must SAY that rather than the old "No UI executable found in
        // bundle payloads", which described a symptom of a scan that no longer exists. The old
        // scan took any .exe payload whose name did not contain "Engine" (last match won) and
        // then fell back to combining the cache directory with an ExePackage's build-machine
        // SourcePath — Path.Combine returns a rooted second argument unchanged, so on a build box
        // that fallback launched the author's own prerequisite exe as the UI.
        var bundlePath = BuildUnsignedMsiOnlyBundle("NoUiMsi");

        var (exitCode, stdErr) = RunCapturingStdErr(
            () => BootstrapperRunner.RunAsync(exePathOverride: bundlePath));

        Assert.NotEqual(0, exitCode);
        Assert.Contains(UiPayload.PackageId, stdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void RunAsync_UiExitsBeforeHandshake_TellsTheUserWhyOnStdErr()
    {
        // WHY: this is what a machine without the .NET Desktop Runtime does. The UI process starts,
        // the host cannot load a runtime, and it exits. Before this test the engine sat on the pipe
        // for a full minute and then returned exit code 1 having printed nothing at all: the reason
        // went into the log file and the outcome object, and the user watching the console saw a
        // one-minute freeze and no explanation.
        //
        // The stand-in UI is a copy of where.exe, which exits with code 2 in about 15 ms when given
        // arguments it does not understand — the same shape as a host that cannot start, without
        // needing a published UI or a broken runtime on the test machine.
        var uiStub = Path.Combine(_tempDir, "ui-stub.exe");
        File.Copy(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"),
            uiStub);

        var payloadPath = Path.Combine(_tempDir, "HandshakeMsi.msi");
        File.WriteAllBytes(payloadPath, RandomNumberGenerator.GetBytes(256));

        var model = new BundleBuilder()
            .Name("HandshakeFailureTest")
            .Manufacturer("Integration Tests")
            .Version("1.0.0")
            .UseSilentUI()
            .Chain(chain => chain.MsiPackage(payloadPath, pkg => pkg.Id("HandshakeMsi").Version("1.0.0")))
            .Build();

        var buildResult = new BundleCompiler { AllowPlaceholderStub = true, UiPath = uiStub }
            .Compile(model, Path.Combine(_tempDir, "out-handshake"));
        Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);

        var (exitCode, stdErr) = RunCapturingStdErr(
            () => BootstrapperRunner.RunAsync(exePathOverride: buildResult.Value));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("exited", stdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x00000002", stdErr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Corrupts the embedded manifest JSON bytes in place (same byte length, so every downstream
    /// offset — payloads, TOC, footer — stays valid) with content that fails JSON deserialization.
    /// Bundle format: <c>[stub][magic 16][manifestLen int32][manifest bytes][payloads][TOC][footer]</c>
    /// — the header magic is the FIRST occurrence of the magic bytes in the file (the footer's copy
    /// comes later, immediately before the trailing TOC offset).
    /// </summary>
    private static void CorruptEmbeddedManifestBytes(string bundlePath)
    {
        var bytes = File.ReadAllBytes(bundlePath);
        var magic = BundleReader.BundleMagic.ToArray();

        var headerMagicPos = bytes.AsSpan().IndexOf(magic);
        Assert.True(headerMagicPos >= 0, "expected to find the FALKBUNDLE header magic");

        var manifestLenPos = headerMagicPos + magic.Length;
        var manifestLen = BitConverter.ToInt32(bytes, manifestLenPos);
        Assert.True(manifestLen > 0, "expected a non-empty embedded manifest to corrupt");

        var manifestBytesPos = manifestLenPos + sizeof(int);
        Array.Fill(bytes, (byte)0x00, manifestBytesPos, manifestLen);

        File.WriteAllBytes(bundlePath, bytes);
    }
}
