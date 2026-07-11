using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Signing;
using FalkForge.Signing.SignServer;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// End-to-end proof of key ROTATION and REVOCATION against a live Keyfactor SignServer instance, beyond the
/// single-key happy path already covered by <see cref="SignServerPodSigningE2ETests"/>.
///
/// <para><b>Two-key provisioning (discovered live).</b> The <c>keyfactor/signserver-ce</c> demo keystore
/// (<c>res/test/dss10/dss10_keystore.p12</c>, password <c>foo123</c>) ships 24 aliases; most are RSA/DSA test
/// keys, but <c>apk00002</c>, <c>code00002</c>, <c>signer00002</c>, <c>sod00002</c> and <c>ts00002</c> are all
/// secp256r1 EC keys (confirmed via <c>keytool -list -v</c> against a throwaway container — everything else
/// enumerated is RSA or DSA and would not round-trip through <see cref="SignServerSignatureProvider"/>'s
/// ECDSA/P1363 conversion). Rather than generate a fresh key at runtime — the CE image's
/// <c>KeystoreCryptoToken</c> over this static PKCS12 file does not expose key generation — this suite reuses
/// a SECOND already-shipped EC key (<c>code00002</c>) as the "new" rotated-to key, alongside <c>apk00002</c>
/// (the "old" key, same one <see cref="SignServerPodSigningE2ETests"/> uses) as the "old" key. Two PlainSigner
/// workers point at the same <c>KeystoreCryptoToken</c> crypto token, one per key, applied via a single
/// <c>bin/signserver setproperties</c> + <c>reload all</c> — the same recipe as the happy-path e2e, just with
/// a second WORKERxx block.</para>
///
/// <para><b>Docker/Podman gate.</b> Shares <see cref="SignServerPodSigningE2ETests.ContainerRuntime"/> so the
/// skip/run decision (and the Ryuk/Linux-daemon guard) is identical to the happy-path e2e: skips (never
/// fails) with no Linux container runtime, runs for real when one is present.</para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class SignServerRotationRevocationE2ETests
{
    private const int SignServerHttpPort = 8080;
    private const string WorkerNameOld = "PlainECDSA_Old";
    private const string WorkerNameNew = "PlainECDSA_New";

    // WORKER11 (old key, apk00002) mirrors SignServerPodSigningE2ETests exactly. WORKER12 (new key,
    // code00002) is the second live-discovered EC alias — see the class doc for how it was found.
    private const string WorkerProperties =
        "WORKER10.TYPE=CRYPTO_WORKER\\n" +
        "WORKER10.IMPLEMENTATION_CLASS=org.signserver.server.signers.CryptoWorker\\n" +
        "WORKER10.CRYPTOTOKEN_IMPLEMENTATION_CLASS=org.signserver.server.cryptotokens.KeystoreCryptoToken\\n" +
        "WORKER10.NAME=CryptoTokenP12\\n" +
        "WORKER10.KEYSTORETYPE=PKCS12\\n" +
        "WORKER10.KEYSTOREPATH=/opt/keyfactor/signserver/res/test/dss10/dss10_keystore.p12\\n" +
        "WORKER10.KEYSTOREPASSWORD=foo123\\n" +
        "WORKER11.TYPE=PROCESSABLE\\n" +
        "WORKER11.IMPLEMENTATION_CLASS=org.signserver.module.cmssigner.PlainSigner\\n" +
        "WORKER11.NAME=" + WorkerNameOld + "\\n" +
        "WORKER11.AUTHTYPE=NOAUTH\\n" +
        "WORKER11.CRYPTOTOKEN=CryptoTokenP12\\n" +
        "WORKER11.DEFAULTKEY=apk00002\\n" +
        "WORKER11.SIGNATUREALGORITHM=SHA256withECDSA\\n" +
        "WORKER11.DISABLEKEYUSAGECOUNTER=true\\n" +
        "WORKER12.TYPE=PROCESSABLE\\n" +
        "WORKER12.IMPLEMENTATION_CLASS=org.signserver.module.cmssigner.PlainSigner\\n" +
        "WORKER12.NAME=" + WorkerNameNew + "\\n" +
        "WORKER12.AUTHTYPE=NOAUTH\\n" +
        "WORKER12.CRYPTOTOKEN=CryptoTokenP12\\n" +
        "WORKER12.DEFAULTKEY=code00002\\n" +
        "WORKER12.SIGNATUREALGORITHM=SHA256withECDSA\\n" +
        "WORKER12.DISABLEKEYUSAGECOUNTER=true\\n";

    /// <summary>
    /// Rotation overlap: a bundle dual-signed by the OLD and NEW live SignServer keys must be accepted by an
    /// engine trusting both, one trusting only the new key (a client that has already migrated), and one
    /// trusting only the old key (a client that has not migrated yet) — proving the trust-leads/signing-follows
    /// overlap actually works with real ECDSA signatures from two separate SignServer workers, not synthetic
    /// in-process keys.
    /// </summary>
    [Fact]
    public async Task Bundle_DualSignedByOldAndNewSignServerKeys_VerifiesUnderEveryRealisticTrustSet()
    {
        var (runtimeAvailable, reason) = await SignServerPodSigningE2ETests.ContainerRuntime.TryEnsureConfiguredAsync();
        Assert.SkipUnless(runtimeAvailable, $"No Docker/Podman container runtime available: {reason}");

        await using var container = BuildContainer();
        await container.StartAsync();
        await ProvisionWorkersAsync(container);

        var baseUrl = $"http://{container.Hostname}:{container.GetMappedPublicPort(SignServerHttpPort)}";
        await WaitForWorkerReadyAsync(baseUrl, WorkerNameOld);
        await WaitForWorkerReadyAsync(baseUrl, WorkerNameNew);

        var tempDir = Path.Combine(Path.GetTempPath(), $"SignServerRotationE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            using var oldKeyProvider = new SignServerSignatureProvider(new SignServerConfig
            {
                BaseUrl = baseUrl,
                Worker = WorkerNameOld,
                AuthMode = SignServerAuthMode.None,
                KeyId = "signserver-old-key"
            });
            using var newKeyProvider = new SignServerSignatureProvider(new SignServerConfig
            {
                BaseUrl = baseUrl,
                Worker = WorkerNameNew,
                AuthMode = SignServerAuthMode.None,
                KeyId = "signserver-new-key"
            });

            var model = BuildModel(tempDir, "RotationBundle", oldKeyProvider, newKeyProvider);
            var compileResult = await new BundleCompiler { AllowPlaceholderStub = true }.CompileAsync(model, Path.Combine(tempDir, "out"));
            Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

            var manifest = ExtractManifest(compileResult.Value);
            Assert.NotNull(manifest.ManifestSignature);

            var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
            Assert.NotNull(envelope);

            // Both real SignServer keys contributed a signature entry, in provider declaration order.
            Assert.Equal(2, envelope!.Signatures.Count);
            var oldFingerprint = envelope.Signatures[0].Fingerprint;
            var newFingerprint = envelope.Signatures[1].Fingerprint;
            Assert.NotEqual(oldFingerprint, newFingerprint);

            // (1) Engine trusts BOTH fingerprints — accepts (either signature validates it).
            Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, Set(oldFingerprint, newFingerprint)).IsSuccess);

            // (2) Engine trusts ONLY the NEW key — a client that has already moved trust to the rotated-to
            // key still accepts the dual-signed release via the new key's real SignServer signature.
            Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, Set(newFingerprint)).IsSuccess);

            // (3) Engine trusts ONLY the OLD key — a client that has not migrated yet still accepts the same
            // release via the old key's real SignServer signature. This is the rotation overlap window.
            Assert.True(IntegrityEnvelopeCodec.VerifyTrusted(envelope, Set(oldFingerprint)).IsSuccess);

            // Negative control: an engine trusting neither real fingerprint rejects with INT001 — proving the
            // three successes above are because the trust set actually matters, not because verification is
            // unconditional.
            var untrusted = IntegrityEnvelopeCodec.VerifyTrusted(envelope, Set("0000000000000000000000000000000000000000000000000000000000000000"));
            Assert.True(untrusted.IsFailure);
            Assert.Contains("INT001", untrusted.Error.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Revocation: a bundle signed by a live SignServer key verifies against a trust set that pins it, but is
    /// rejected (INT001) once that key's fingerprint is locally revoked — even though the key remains in the
    /// baked trusted set (revocation overrides stale baked trust). A second bundle signed by a DIFFERENT,
    /// non-revoked SignServer key still verifies, proving revocation is scoped to the specific key, not a
    /// blanket rejection. Driven at the real runtime verify layer (<see cref="BundleTrustVerifier"/> →
    /// <see cref="SignedPayloadTocVerifier"/>), the same call the engine's extract path makes, by injecting a
    /// revoked-fingerprint set the way a persisted local trust store would.
    /// </summary>
    [Fact]
    public async Task Bundle_SignedByRevokedSignServerKey_IsRejected_ButNonRevokedKeyStillVerifies()
    {
        var (runtimeAvailable, reason) = await SignServerPodSigningE2ETests.ContainerRuntime.TryEnsureConfiguredAsync();
        Assert.SkipUnless(runtimeAvailable, $"No Docker/Podman container runtime available: {reason}");

        await using var container = BuildContainer();
        await container.StartAsync();
        await ProvisionWorkersAsync(container);

        var baseUrl = $"http://{container.Hostname}:{container.GetMappedPublicPort(SignServerHttpPort)}";
        await WaitForWorkerReadyAsync(baseUrl, WorkerNameOld);
        await WaitForWorkerReadyAsync(baseUrl, WorkerNameNew);

        var tempDir = Path.Combine(Path.GetTempPath(), $"SignServerRevocationE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            using var revokedKeyProvider = new SignServerSignatureProvider(new SignServerConfig
            {
                BaseUrl = baseUrl,
                Worker = WorkerNameOld,
                AuthMode = SignServerAuthMode.None,
                KeyId = "signserver-to-be-revoked"
            });
            using var goodKeyProvider = new SignServerSignatureProvider(new SignServerConfig
            {
                BaseUrl = baseUrl,
                Worker = WorkerNameNew,
                AuthMode = SignServerAuthMode.None,
                KeyId = "signserver-not-revoked"
            });

            // Two separately signed bundles — one per key — so revoking the first key's fingerprint can be
            // shown to leave the second bundle's (different key's) verification untouched.
            var revokedBundleModel = BuildModel(tempDir, "RevokedKeyBundle", revokedKeyProvider);
            var revokedCompile = await new BundleCompiler { AllowPlaceholderStub = true }.CompileAsync(
                revokedBundleModel, Path.Combine(tempDir, "revoked-out"));
            Assert.True(revokedCompile.IsSuccess, revokedCompile.IsFailure ? revokedCompile.Error.Message : null);

            var goodBundleModel = BuildModel(tempDir, "GoodKeyBundle", goodKeyProvider);
            var goodCompile = await new BundleCompiler { AllowPlaceholderStub = true }.CompileAsync(
                goodBundleModel, Path.Combine(tempDir, "good-out"));
            Assert.True(goodCompile.IsSuccess, goodCompile.IsFailure ? goodCompile.Error.Message : null);

            var revokedContent = ExtractContent(revokedCompile.Value);
            var revokedFingerprint = SingleFingerprint(revokedContent);

            var goodContent = ExtractContent(goodCompile.Value);
            var goodFingerprint = SingleFingerprint(goodContent);
            Assert.NotEqual(revokedFingerprint, goodFingerprint);

            // Baseline: with no local revocations, the key that is about to be revoked verifies normally.
            var baseline = BundleTrustVerifier.VerifyBundleContent(revokedContent, Set(revokedFingerprint));
            Assert.True(baseline.IsSuccess, baseline.IsFailure ? baseline.Error.Message : null);

            // The trust store now records that key's fingerprint as revoked (simulating an operator response
            // to a compromised SignServer key). The same bundle is rejected with INT001 even though its key
            // is still in the baked trusted set — revocation overrides stale baked trust.
            var revoked = BundleTrustVerifier.VerifyBundleContent(
                revokedContent, Set(revokedFingerprint), revokedFingerprints: Set(revokedFingerprint));
            Assert.True(revoked.IsFailure);
            Assert.Contains("INT001", revoked.Error.Message);

            // A bundle signed by a DIFFERENT, non-revoked key still verifies under the same revoked set —
            // revocation is scoped to the specific fingerprint, not a blanket rejection of every signature.
            var stillGood = BundleTrustVerifier.VerifyBundleContent(
                goodContent, Set(goodFingerprint), revokedFingerprints: Set(revokedFingerprint));
            Assert.True(stillGood.IsSuccess, stillGood.IsFailure ? stillGood.Error.Message : null);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static HashSet<string> Set(params string[] fingerprints) =>
        new(fingerprints, StringComparer.OrdinalIgnoreCase);

    private static string SingleFingerprint(FalkForge.Engine.Protocol.Bundle.BundleContent content)
    {
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.ManifestJsonBytes!);
        Assert.NotNull(manifest);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest!.ManifestSignature!);
        Assert.NotNull(envelope);
        return Assert.Single(envelope!.Signatures).Fingerprint;
    }

    private static IContainer BuildContainer() =>
        new ContainerBuilder()
            .WithImage("keyfactor/signserver-ce:latest")
            .WithPortBinding(SignServerHttpPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(SignServerHttpPort)
                .ForPath("/signserver/")))
            .Build();

    private static async Task ProvisionWorkersAsync(IContainer container)
    {
        var write = await container.ExecAsync(
            ["sh", "-c", $"printf '%b' \"{WorkerProperties}\" > /tmp/falk-rotation-worker.properties"]);
        Assert.Equal(0L, write.ExitCode);

        var apply = await container.ExecAsync(["bin/signserver", "setproperties", "/tmp/falk-rotation-worker.properties"]);
        Assert.Equal(0L, apply.ExitCode);

        var reload = await container.ExecAsync(["bin/signserver", "reload", "all"]);
        Assert.Equal(0L, reload.ExitCode);
    }

    /// <summary>Polls the named worker until it answers a real signature — mirrors the happy-path e2e's guard.</summary>
    private static async Task WaitForWorkerReadyAsync(string baseUrl, string workerName)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var probeBody = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["encoding"] = "BASE64",
            ["data"] = Convert.ToBase64String("ready-probe"u8.ToArray())
        });

        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var content = new StringContent(probeBody, System.Text.Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(
                    new Uri($"/signserver/rest/v1/workers/{workerName}/process", UriKind.Relative), content);
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
                // container still warming up
            }

            await Task.Delay(2000);
        }

        Assert.Fail($"SignServer worker '{workerName}' did not become ready in time.");
    }

    private static BundleModel BuildModel(string tempDir, string bundleName, params ISignatureProvider[] providers)
    {
        var payloadPath = Path.Combine(tempDir, $"{bundleName}.msi");
        File.WriteAllText(payloadPath, $"e2e-payload-content-{bundleName}");

        return new BundleModel
        {
            Name = bundleName,
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new()
                {
                    Id = "PkgA",
                    SourcePath = payloadPath,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "PkgA"
                }
            }.AsReadOnly(),
            Integrity = new IntegrityConfiguration
            {
                SignatureProviders = providers
            }
        };
    }

    private static InstallerManifest ExtractManifest(string bundlePath)
    {
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(ExtractContent(bundlePath).ManifestJsonBytes!);
        Assert.NotNull(manifest);
        return manifest!;
    }

    private static FalkForge.Engine.Protocol.Bundle.BundleContent ExtractContent(string bundlePath)
    {
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        return content.Value;
    }
}
