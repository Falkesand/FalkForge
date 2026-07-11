using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine;
using FalkForge.Engine.Download;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// C14 Stage 3 FIX 1 — end-to-end proof that the already-installed, already-trusted engine verifies a
/// <b>downloaded</b> update bundle IN-PROCESS before launching it. Drives the real
/// <see cref="UpdateDownloader"/> with a fake launcher and the real <see cref="StagedUpdateVerifier"/> over
/// real compiled bundles. The trust decision is made by the running engine over the staged bytes — never
/// delegated to the downloaded artifact (which, being the attacker's own binary with its own engine, would
/// ignore a passed <c>--require-signed</c> flag).
///
/// <para>The four cases: a validly-trusted-signed update launches; an unsigned (INT007), an
/// untrusted-key-signed (INT001), and a payload-tampered (INT006) update are all REJECTED — the launcher's
/// <c>Launch</c> is never called and the staged bundle is discarded.</para>
/// </summary>
public sealed class UpdateDownloaderTrustTests
{
    private sealed class FakeLauncher : IUpdateLauncher
    {
        public List<string> Launched { get; } = [];
        public Result<Unit> Launch(string updatePath)
        {
            Launched.Add(updatePath);
            return Unit.Value;
        }
    }

    /// <summary>
    /// Drives a full AutoUpdate download+verify cycle over <paramref name="sourceBundle"/>, pinning
    /// <paramref name="trustedFingerprints"/>. Returns whether the launcher fired and whether the staged
    /// bundle survived (a rejected update is discarded).
    /// </summary>
    private static (bool launched, bool stagedSurvived) RunAutoUpdate(
        string sourceBundle, IReadOnlySet<string> trustedFingerprints, string cacheDir)
    {
        var launcher = new FakeLauncher();
        string? stagedPath = null;

        // The "download" copies the source bundle to the path StartAsync chose (the real staging move).
        Task<Result<string>> Download(
            string url, string sha, string dest,
            IProgress<(long, long)>? progress, bool resume, long? expectedSize, CancellationToken ct)
        {
            File.Copy(sourceBundle, dest, overwrite: true);
            stagedPath = dest;
            return Task.FromResult(Result<string>.Success(dest));
        }

        // The real in-process trust gate, require-signed, pinned to the supplied set.
        Result<Unit> Verify(string path) => StagedUpdateVerifier.Verify(path, trustedFingerprints, 0, null);

        var downloader = new UpdateDownloader(
            Download,
            (_, _) => Task.CompletedTask,
            new FalkForge.Diagnostics.NullLogger(),
            UpdatePolicy.AutoUpdate,
            allowResume: false,
            launcher: launcher,
            promptBeforeAutoUpdate: false,
            showDownloadErrors: false,
            verifyStagedBundle: Verify);

        var update = new UpdateInfo("2.0.0", "https://example.com/v2.exe", "sha-from-feed", null, null);
        downloader.StartAsync(update, cacheDir, CancellationToken.None).GetAwaiter().GetResult();

        var survived = stagedPath is not null && File.Exists(stagedPath);
        return (launcher.Launched.Count > 0, survived);
    }

    private static string SignedBundleFingerprint(string bundlePath)
    {
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!);
        Assert.NotNull(manifest!.ManifestSignature);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
        Assert.NotNull(envelope);
        return envelope!.Signatures[0].Fingerprint;
    }

    [Fact]
    public void DownloadedUpdate_OnlyTrustedSignedLaunches_AttacksRejectedAndDiscarded()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"falk-update-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var cacheDir = Path.Combine(dir, "cache");
        Directory.CreateDirectory(cacheDir);
        try
        {
            var originalBytes = RandomNumberGenerator.GetBytes(512);
            var msiPath = Path.Combine(dir, "App.msi");
            File.WriteAllBytes(msiPath, originalBytes);

            // 1. A validly integrity-signed bundle (ephemeral key). Its real fingerprint is the pin.
            var signedModel = new BundleBuilder()
                .Name("UpdTrust").Manufacturer("Integration Tests").Version("2.0.0")
                .UseSilentUI()
                .Integrity(i => { })
                .Chain(c => c.MsiPackage(msiPath, p => p.Id("AppMsi").Version("2.0.0")))
                .Build();
            var signedBuild = new BundleCompiler { AllowPlaceholderStub = true }.Compile(signedModel, Path.Combine(dir, "signed"));
            Assert.True(signedBuild.IsSuccess, signedBuild.IsFailure ? signedBuild.Error.Message : null);
            var signedBundle = signedBuild.Value;
            var trustedFp = SignedBundleFingerprint(signedBundle);
            var trusted = new HashSet<string>(new[] { trustedFp }, StringComparer.OrdinalIgnoreCase);
            var untrusted = new HashSet<string>(
                new[] { "00000000000000000000000000000000000000000000000000000000000000FF" },
                StringComparer.OrdinalIgnoreCase);

            // 2. An unsigned bundle (no Integrity()).
            var unsignedModel = new BundleBuilder()
                .Name("UpdUnsigned").Manufacturer("Integration Tests").Version("2.0.0")
                .UseSilentUI()
                .Chain(c => c.MsiPackage(msiPath, p => p.Id("AppMsi").Version("2.0.0")))
                .Build();
            var unsignedBuild = new BundleCompiler { AllowPlaceholderStub = true }.Compile(unsignedModel, Path.Combine(dir, "unsigned"));
            Assert.True(unsignedBuild.IsSuccess, unsignedBuild.IsFailure ? unsignedBuild.Error.Message : null);
            var unsignedBundle = unsignedBuild.Value;

            // 3. A payload-tampered clone of the signed bundle: original signed manifest, tampered payload,
            //    matching (rewritten) TOC hash — the classic post-signing overlay tamper.
            var tamperedBundle = BuildTamperedClone(dir, signedBundle, originalBytes);

            // --- valid: trusted signature => launches, staged bundle kept ---
            var valid = RunAutoUpdate(signedBundle, trusted, cacheDir);
            Assert.True(valid.launched, "a validly-trusted-signed update must launch");
            Assert.True(valid.stagedSurvived, "a launched update must be kept");

            // --- unsigned: require-signed => INT007, no launch, discarded ---
            var unsigned = RunAutoUpdate(unsignedBundle, trusted, cacheDir);
            Assert.False(unsigned.launched, "an unsigned update must not launch");
            Assert.False(unsigned.stagedSurvived, "a rejected unsigned update must be discarded");

            // --- untrusted key: signature valid but key not pinned => INT001, no launch, discarded ---
            var untrustedRun = RunAutoUpdate(signedBundle, untrusted, cacheDir);
            Assert.False(untrustedRun.launched, "an untrusted-key-signed update must not launch");
            Assert.False(untrustedRun.stagedSurvived, "a rejected untrusted update must be discarded");

            // --- tampered payload: trusted signature but TOC tamper => INT006, no launch, discarded ---
            var tampered = RunAutoUpdate(tamperedBundle, trusted, cacheDir);
            Assert.False(tampered.launched, "a payload-tampered update must not launch");
            Assert.False(tampered.stagedSurvived, "a rejected tampered update must be discarded");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string BuildTamperedClone(string dir, string signedBundle, byte[] originalBytes)
    {
        var signedContent = PayloadEmbedder.Extract(signedBundle);
        Assert.True(signedContent.IsSuccess, signedContent.IsFailure ? signedContent.Error.Message : null);
        var signedManifest = JsonSerializer.Deserialize<InstallerManifest>(signedContent.Value.ManifestJsonBytes!)!;

        var tamperedBytes = (byte[])originalBytes.Clone();
        tamperedBytes[0] ^= 0xFF;
        var tamperedMsi = Path.Combine(dir, "App.tampered.msi");
        File.WriteAllBytes(tamperedMsi, tamperedBytes);
        var tamperedHash = Convert.ToHexString(SHA256.HashData(tamperedBytes));

        var stubPath = Path.Combine(dir, "stub.bin");
        File.WriteAllBytes(stubPath, []);
        var attackerBundle = Path.Combine(dir, "attacker.exe");

        var tamperedPayload = new PayloadEntry
        {
            PackageId = "AppMsi",
            SourcePath = tamperedMsi,
            OriginalSize = tamperedBytes.Length,
            Sha256Hash = tamperedHash
        };

        var embed = new PayloadEmbedder().Embed(stubPath, attackerBundle, signedManifest, new[] { tamperedPayload });
        Assert.True(embed.IsSuccess, embed.IsFailure ? embed.Error.Message : null);
        return attackerBundle;
    }
}
