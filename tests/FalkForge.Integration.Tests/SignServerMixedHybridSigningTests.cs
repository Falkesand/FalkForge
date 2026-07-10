using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Signing;
using FalkForge.Signing.SignServer;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// The RECOMMENDED INTERIM posture for remote-signed hybrid bundles (PQ-hybrid Stage 4 assessment,
/// design §8.6): SignServer signs the CLASSICAL half, a local ML-DSA-65 PEM key signs the
/// POST-QUANTUM half — two independent <see cref="ISignatureProvider"/> entries over the identical
/// canonical message. This exists because SignServer's PlainSigner, while it does support pure
/// ML-DSA (SignServer CE 7.1+), offers no FIPS 204 context-string property: its ML-DSA signatures
/// are made with the empty context and can therefore never satisfy FalkForge's frozen
/// <c>"falkforge/manifest"</c> manifest context — so the PQ half must be signed locally, where the
/// context is applied, until a remote backend can carry it.
///
/// <para>The SignServer half runs against the same deterministic in-memory HTTP stub the provider
/// unit tests use (a real P-256 key + self-signed certificate standing in for the PlainSigner
/// worker, signing the actual request bytes SHA256withECDSA/DER exactly as the live worker does),
/// so this proof needs no container while still exercising the provider's real REST + DER→P1363
/// boundary inside the real async compile pipeline.</para>
/// </summary>
public sealed class SignServerMixedHybridSigningTests : IDisposable
{
    private readonly string _tempDir;

    public SignServerMixedHybridSigningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SignServerMixedHybrid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// A deterministic SignServer PlainSigner stand-in: signs whatever bytes the provider actually
    /// POSTs (SHA256withECDSA, DER) with a real P-256 key and returns the worker's response shape
    /// (base64 DER signature + base64 DER certificate), like the live REST endpoint.
    /// </summary>
    private sealed class FakeSignServerHandler : HttpMessageHandler
    {
        private readonly ECDsa _key;
        private readonly X509Certificate2 _certificate;

        public FakeSignServerHandler(ECDsa key)
        {
            _key = key;
            var request = new CertificateRequest("CN=SignServer PlainSigner", key, HashAlgorithmName.SHA256);
            _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var message = Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!);

            var der = _key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            var responseJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["data"] = Convert.ToBase64String(der),
                ["signerCertificate"] = Convert.ToBase64String(_certificate.RawData)
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _certificate.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task MixedProviders_SignServerClassicalPlusLocalPq_CompileToCompanionPinnedHybridBundle()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // The publisher's hybrid identity: the classical key lives on SignServer (never leaves the
        // service), the ML-DSA companion is a local PEM applied with the frozen manifest context.
        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        using var handler = new FakeSignServerHandler(serverKey);
        using var signServerProvider = new SignServerSignatureProvider(new SignServerConfig
        {
            BaseUrl = "https://sign.example.test:8443",
            Worker = "PlainECDSA",
            AuthMode = SignServerAuthMode.None,
            KeyId = "signserver-classical"
        }, handler);

        var payloadPath = Path.Combine(_tempDir, "App.msi");
        File.WriteAllBytes(payloadPath, RandomNumberGenerator.GetBytes(512));

        var model = new BundleModel
        {
            Name = "MixedHybrid",
            Manufacturer = "Integration Tests",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new()
                {
                    Id = "AppMsi",
                    SourcePath = payloadPath,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "AppMsi"
                }
            }.AsReadOnly(),
            Integrity = new IntegrityConfiguration
            {
                SignatureProviders = new ISignatureProvider[]
                {
                    signServerProvider,
                    MLDsaPemSignatureProvider.FromPemContent(mldsa.ExportPkcs8PrivateKeyPem())
                }
            }
        };

        // SignServer is a genuinely asynchronous backend — the async compile pipeline is required.
        var compileResult = await new BundleCompiler().CompileAsync(model, Path.Combine(_tempDir, "out"));
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

        var contentResult = PayloadEmbedder.Extract(compileResult.Value);
        Assert.True(contentResult.IsSuccess, contentResult.IsFailure ? contentResult.Error.Message : null);
        var content = contentResult.Value;

        // The envelope is a genuine hybrid: the SignServer-backed classical entry first (its
        // fingerprint is the worker certificate's SPKI fingerprint), the local ML-DSA companion second.
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.ManifestJsonBytes!)!;
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!)!;
        Assert.Equal(2, envelope.Signatures.Count);
        var classicalFingerprint = Convert.ToHexString(
            SHA256.HashData(serverKey.ExportSubjectPublicKeyInfo()));
        var pqFingerprint = Convert.ToHexString(SHA256.HashData(mldsa.ExportSubjectPublicKeyInfo()));
        Assert.Null(envelope.Signatures[0].Algorithm);
        Assert.Equal(classicalFingerprint, envelope.Signatures[0].Fingerprint);
        Assert.Equal(IntegrityEnvelopeCodec.MlDsa65AlgorithmId, envelope.Signatures[1].Algorithm);
        Assert.Equal(pqFingerprint, envelope.Signatures[1].Fingerprint);

        // The mixed pair passes the engine's real verify layer with the companion pinned: the
        // remote-signed classical entry counts only because the locally-signed ML-DSA companion
        // verifies under the frozen manifest context.
        var trusted = new HashSet<string>([classicalFingerprint], StringComparer.OrdinalIgnoreCase);
        var pqPolicy = new PqCompanionPolicy
        {
            Companions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [classicalFingerprint] = pqFingerprint
            }
        };
        var verified = BundleTrustVerifier.VerifyBundleContent(content, trusted, pqPolicy: pqPolicy);
        Assert.True(verified.IsSuccess, verified.IsFailure ? verified.Error.Message : null);

        // Anti-strip holds for the mixed posture too: dropping the local ML-DSA entry leaves the
        // SignServer classical signature unable to satisfy its pinned companion — INT011.
        var strippedManifest = manifest with
        {
            ManifestSignature = IntegrityEnvelopeCodec.Serialize(new ManifestSignatureEnvelope
            {
                Version = envelope.Version,
                Algorithm = envelope.Algorithm,
                Files = envelope.Files,
                Signatures = [envelope.Signatures[0]],
                Epoch = envelope.Epoch,
                Revoked = envelope.Revoked
            })
        };
        var strippedContent = new BundleContent
        {
            TocEntries = content.TocEntries,
            BundlePath = content.BundlePath,
            ManifestJsonBytes = JsonSerializer.SerializeToUtf8Bytes(strippedManifest)
        };
        var stripped = BundleTrustVerifier.VerifyBundleContent(strippedContent, trusted, pqPolicy: pqPolicy);
        Assert.True(stripped.IsFailure);
        Assert.Contains("INT011", stripped.Error.Message, StringComparison.Ordinal);
    }
}
