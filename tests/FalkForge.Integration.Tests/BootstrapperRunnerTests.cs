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
/// Deliberately stops at three early-return failure branches (corrupt embedded manifest, the signed-payload
/// trust gate, and no UI executable found) — reaching the happy path requires actually spawning and driving
/// a real UI process (<see cref="UiProcessLauncher.TryStartUiProcess"/> / <see cref="EngineSession.RunUntilShutdown"/>),
/// which needs a real UI executable and is covered by the opt-in (<c>FALKFORGE_E2E</c>) end-to-end suite, not
/// by these fast unit tests.
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
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best effort */ }
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
    public void RunAsync_NoUiExecutableNoExePackageFallback_ReturnsFailureExitCode()
    {
        // WHY: an MSI-only bundle (no embedded .exe payload, no ExePackage declared in the manifest)
        // has nothing for the bootstrapper to launch as the UI. RunAsync must fail loud with the
        // documented "No UI executable found" message instead of launching nothing or crashing later
        // when it tries to start a process that was never resolved.
        var bundlePath = BuildUnsignedMsiOnlyBundle("NoUiMsi");

        var (exitCode, stdErr) = RunCapturingStdErr(
            () => BootstrapperRunner.RunAsync(exePathOverride: bundlePath));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("No UI executable found", stdErr, StringComparison.Ordinal);
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
