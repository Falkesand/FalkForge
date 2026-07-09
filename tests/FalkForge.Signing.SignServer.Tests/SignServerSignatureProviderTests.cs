using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Signing;
using FalkForge.Signing.SignServer;
using Xunit;

namespace FalkForge.Signing.SignServer.Tests;

/// <summary>
/// Pins the <see cref="SignServerSignatureProvider"/> REST contract and its boundary normalization with a
/// deterministic in-memory HTTP handler (no network, no Docker). The centrepiece is the end-to-end proof:
/// a real P-256 key on the "server" produces a real DER signature + self-signed certificate, and the
/// provider's converted P1363 signature is verified through the real <see cref="IntegrityEnvelopeCodec"/> —
/// the exact code the engine runs — so a green test proves DER→P1363 conversion + certificate parsing +
/// wire compatibility together, not a re-implementation that only agrees with itself.
/// </summary>
public sealed class SignServerSignatureProviderTests
{
    /// <summary>A deterministic <see cref="HttpMessageHandler"/> that records the request and returns a canned response.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, LastBody);
        }
    }

    /// <summary>An ECDSA P-256 signer + its self-signed certificate, standing in for a SignServer worker key.</summary>
    private sealed record ServerKey(ECDsa Key, X509Certificate2 Certificate)
    {
        public static ServerKey Create()
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=SignServer PlainSigner", key, HashAlgorithmName.SHA256);
            var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return new ServerKey(key, cert);
        }

        /// <summary>Signs <paramref name="message"/> exactly as SHA256withECDSA does: hash then sign, DER-encoded.</summary>
        public string SignDerBase64(ReadOnlySpan<byte> message) =>
            Convert.ToBase64String(Key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

        public string CertificateDerBase64() => Convert.ToBase64String(Certificate.RawData);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static string ProcessResponseJson(string dataBase64, string certBase64) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["data"] = dataBase64,
            ["signerCertificate"] = certBase64
        });

    private static SignServerConfig Config(SignServerAuthMode auth = SignServerAuthMode.None) => new()
    {
        BaseUrl = "https://sign.example.test:8443",
        Worker = "PlainECDSA",
        AuthMode = auth,
        KeyId = "worker-plain-ecdsa"
    };

    [Fact]
    public async Task SignAsync_PostsRawBase64MessageToTheProcessEndpoint()
    {
        var server = ServerKey.Create();
        var message = new byte[] { 1, 2, 3, 4, 5 };
        StubHandler? captured = null;
        var handler = new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, ProcessResponseJson(server.SignDerBase64(message), server.CertificateDerBase64())));
        captured = handler;

        using var provider = new SignServerSignatureProvider(Config(), handler);
        var result = await provider.SignAsync(message);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(captured.LastRequest);
        Assert.Equal(HttpMethod.Post, captured.LastRequest!.Method);
        Assert.Equal(
            "https://sign.example.test:8443/signserver/rest/v1/workers/PlainECDSA/process",
            captured.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/json", captured.LastRequest.Content!.Headers.ContentType!.MediaType);

        using var doc = JsonDocument.Parse(captured.LastBody);
        Assert.Equal("BASE64", doc.RootElement.GetProperty("encoding").GetString());
        // The raw canonical bytes are sent (SignServer hashes server-side) — NOT a pre-computed digest.
        Assert.Equal(Convert.ToBase64String(message), doc.RootElement.GetProperty("data").GetString());
    }

    [Fact]
    public async Task SignAsync_ConvertedP1363Signature_VerifiesThroughTheRealCodec()
    {
        // End-to-end boundary proof: server returns DER + cert; the provider must hand back a P1363 signature
        // and the certificate's SPKI such that a real integrity envelope built from them VERIFIES.
        var server = ServerKey.Create();
        var files = new List<ManifestFileEntry> { new() { Name = "PkgA", Sha256 = "AABBCC" } };
        var message = IntegrityEnvelopeCodec.ComputeSignedBytes(files);

        var handler = new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, ProcessResponseJson(server.SignDerBase64(message), server.CertificateDerBase64())));

        using var provider = new SignServerSignatureProvider(Config(), handler);
        var result = await provider.SignAsync(message);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var signature = result.Value;

        // P1363 encoding is locked to 64 bytes for P-256 and the SPKI is the certificate's public key.
        Assert.Equal(64, signature.Signature.Length);
        Assert.Equal(server.Certificate.PublicKey.ExportSubjectPublicKeyInfo(), signature.SubjectPublicKeyInfo);
        Assert.Equal("worker-plain-ecdsa", signature.KeyId);

        var envelope = new ManifestSignatureEnvelope
        {
            Version = IntegrityEnvelopeCodec.CurrentVersion,
            Algorithm = IntegrityEnvelopeCodec.AlgorithmId,
            Files = files,
            Signatures =
            [
                new SignatureEntry
                {
                    KeyId = signature.KeyId,
                    Fingerprint = IntegrityEnvelopeCodec.ComputeFingerprint(signature.SubjectPublicKeyInfo),
                    PublicKey = Convert.ToBase64String(signature.SubjectPublicKeyInfo),
                    Signature = Convert.ToBase64String(signature.Signature)
                }
            ],
            Epoch = 0,
            Revoked = []
        };

        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
    }

    [Fact]
    public async Task SignAsync_UnexpectedSignatureEncoding_FailsLoudViaSniffGuard()
    {
        // Server hands back a value that is neither a 64-byte P1363 signature nor a DER SEQUENCE. The
        // sniff guard must reject it (SGN022) rather than emit a garbage signature.
        var server = ServerKey.Create();
        var junk = new byte[40];
        RandomNumberGenerator.Fill(junk);
        junk[0] = 0x11;

        var handler = new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, ProcessResponseJson(Convert.ToBase64String(junk), server.CertificateDerBase64())));

        using var provider = new SignServerSignatureProvider(Config(), handler);
        var result = await provider.SignAsync(new byte[] { 9, 9, 9 });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN022", result.Error.Message);
    }

    [Fact]
    public async Task SignAsync_BearerAuth_SendsBearerHeader()
    {
        var server = ServerKey.Create();
        var message = new byte[] { 7 };
        var handler = new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, ProcessResponseJson(server.SignDerBase64(message), server.CertificateDerBase64())));
        var config = Config(SignServerAuthMode.Bearer) with { BearerToken = "abc.def.ghi" };

        using var provider = new SignServerSignatureProvider(config, handler);
        var result = await provider.SignAsync(message);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("abc.def.ghi", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SignAsync_BasicAuth_SendsBase64UserPassword()
    {
        var server = ServerKey.Create();
        var message = new byte[] { 7 };
        var handler = new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, ProcessResponseJson(server.SignDerBase64(message), server.CertificateDerBase64())));
        var config = Config(SignServerAuthMode.Basic) with { BasicUsername = "alice", BasicPassword = "s3cr3t" };

        using var provider = new SignServerSignatureProvider(config, handler);
        var result = await provider.SignAsync(message);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("Basic", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("alice:s3cr3t")),
            handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SignAsync_NoAuth_SendsNoAuthorizationHeader()
    {
        var server = ServerKey.Create();
        var message = new byte[] { 7 };
        var handler = new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, ProcessResponseJson(server.SignDerBase64(message), server.CertificateDerBase64())));

        using var provider = new SignServerSignatureProvider(Config(), handler);
        var result = await provider.SignAsync(message);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task SignAsync_NonSuccessStatus_FailsWithSgn020()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.Unauthorized, "{}"));

        using var provider = new SignServerSignatureProvider(Config(), handler);
        var result = await provider.SignAsync(new byte[] { 1 });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN020", result.Error.Message);
    }

    [Fact]
    public async Task SignAsync_MissingResponseFields_FailsWithSgn021()
    {
        var handler = new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new Dictionary<string, string> { ["data"] = "" })));

        using var provider = new SignServerSignatureProvider(Config(), handler);
        var result = await provider.SignAsync(new byte[] { 1 });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN021", result.Error.Message);
    }

    [Fact]
    public async Task SignAsync_UnparseableCertificate_FailsWithSgn023()
    {
        var server = ServerKey.Create();
        var message = new byte[] { 5 };
        var notACert = Convert.ToBase64String(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        var handler = new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, ProcessResponseJson(server.SignDerBase64(message), notACert)));

        using var provider = new SignServerSignatureProvider(Config(), handler);
        var result = await provider.SignAsync(message);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN023", result.Error.Message);
    }

    [Fact]
    public void CreateDefaultHandler_ClientCertMode_AttachesClientCertificate()
    {
        var server = ServerKey.Create();
        var config = Config(SignServerAuthMode.ClientCert) with { ClientCertificate = server.Certificate };

        using var handler = SignServerSignatureProvider.CreateDefaultHandler(config);

        Assert.Equal(ClientCertificateOption.Manual, handler.ClientCertificateOptions);
        var attached = Assert.Single(handler.ClientCertificates.Cast<X509Certificate2>());
        Assert.Equal(server.Certificate.Thumbprint, attached.Thumbprint);
        Assert.True(handler.CheckCertificateRevocationList);
    }

    [Fact]
    public void Constructor_MissingBaseUrlOrWorker_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new SignServerSignatureProvider(new SignServerConfig { BaseUrl = "", Worker = "w" }));
        Assert.Throws<ArgumentException>(() =>
            new SignServerSignatureProvider(new SignServerConfig { BaseUrl = "https://x", Worker = "" }));
    }
}
