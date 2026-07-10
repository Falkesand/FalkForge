using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FalkForge.Signing;

namespace FalkForge.Signing.SignServer;

/// <summary>
/// An <see cref="ISignatureProvider"/> that signs the canonical manifest message with a private key that
/// stays on a Keyfactor SignServer instance (PlainSigner worker, SHA256withECDSA). This is a genuinely
/// asynchronous backend — it performs network I/O — so it must be driven through the async build pipeline
/// (<c>Installer.BuildBundleAsync</c> → <c>BundleCompiler.CompileAsync</c>), not the synchronous one.
///
/// <para><b>Boundary normalization.</b> SignServer hashes the supplied bytes internally and returns an
/// ASN.1/DER <c>SEQUENCE{r,s}</c> signature plus the signer's X.509 certificate. This provider converts the
/// signature to IEEE&#160;P1363 (<see cref="EcdsaSignatureFormatConverter"/>) and extracts the certificate's
/// SubjectPublicKeyInfo, so the on-disk envelope keeps FalkForge's single canonical encoding and the engine
/// verifier stays backend-agnostic. Send <i>raw canonical bytes</i> — never a pre-computed digest — because
/// SHA256withECDSA hashes server-side.</para>
/// </summary>
public sealed class SignServerSignatureProvider : ISignatureProvider, IDisposable
{
    private readonly SignServerConfig _config;
    private readonly HttpClient _httpClient;
    private readonly Uri _processUri;
    private bool _disposed;

    /// <summary>
    /// Creates a provider for <paramref name="config"/>. When <paramref name="handler"/> is supplied it is
    /// used verbatim (and left for the caller to dispose) — the injection seam that makes the provider
    /// unit-testable and lets callers wire custom TLS. Otherwise the provider builds its own handler,
    /// attaching the client certificate for <see cref="SignServerAuthMode.ClientCert"/>.
    /// </summary>
    public SignServerSignatureProvider(SignServerConfig config, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
            throw new ArgumentException("SignServer base URL must be set.", nameof(config));
        if (string.IsNullOrWhiteSpace(config.Worker))
            throw new ArgumentException("SignServer worker must be set.", nameof(config));

        // Deliberately NOT rejected here: an http:// BaseUrl and/or AuthMode.None. Local
        // SignServer CE test containers legitimately run http + NOAUTH (the e2e tests rely on
        // it), so hard-failing would break real flows. Production guidance (https + mTLS or
        // Bearer) lives on SignServerConfig.BaseUrl / SignServerConfig.AuthMode; the install-time
        // blast radius of a spoofed endpoint is bounded because only keys in the engine's baked
        // trusted set are ever accepted.
        _config = config;
        _processUri = BuildProcessUri(config);

        _httpClient = handler is not null
            ? new HttpClient(handler, disposeHandler: false)
            : new HttpClient(CreateDefaultHandler(config), disposeHandler: true);
    }

    /// <inheritdoc />
    public async ValueTask<Result<ProviderSignature>> SignAsync(
        ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var requestBody = new SignServerProcessRequest
        {
            Encoding = "BASE64",
            // Raw canonical bytes: PlainSigner (SHA256withECDSA) hashes server-side. Do NOT send a digest.
            Data = Convert.ToBase64String(message.Span)
        };
        var json = JsonSerializer.Serialize(requestBody, SignServerJsonContext.Default.SignServerProcessRequest);

        SignServerProcessResponse? parsed;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _processUri)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            ApplyAuthentication(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Result<ProviderSignature>.Failure(ErrorKind.SecurityError,
                    $"SGN020: SignServer worker '{_config.Worker}' returned HTTP {(int)response.StatusCode}.");

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            parsed = JsonSerializer.Deserialize(body, SignServerJsonContext.Default.SignServerProcessResponse);
        }
        catch (HttpRequestException ex)
        {
            return Result<ProviderSignature>.Failure(ErrorKind.SecurityError,
                $"SGN020: SignServer request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<ProviderSignature>.Failure(ErrorKind.SecurityError,
                $"SGN020: SignServer request timed out: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return Result<ProviderSignature>.Failure(ErrorKind.SecurityError,
                $"SGN021: SignServer response was not valid JSON: {ex.Message}");
        }

        if (parsed is null || string.IsNullOrEmpty(parsed.Data) || string.IsNullOrEmpty(parsed.SignerCertificate))
            return Result<ProviderSignature>.Failure(ErrorKind.SecurityError,
                "SGN021: SignServer response is missing the signature 'data' or 'signerCertificate' field.");

        byte[] rawSignature;
        byte[] certificateDer;
        try
        {
            rawSignature = Convert.FromBase64String(parsed.Data);
            certificateDer = Convert.FromBase64String(parsed.SignerCertificate);
        }
        catch (FormatException ex)
        {
            return Result<ProviderSignature>.Failure(ErrorKind.SecurityError,
                $"SGN021: SignServer response contained non-base64 data: {ex.Message}");
        }

        var normalized = EcdsaSignatureFormatConverter.NormalizeToP1363(rawSignature);
        if (normalized.IsFailure)
            return Result<ProviderSignature>.Failure(normalized.Error);

        var spkiResult = ExtractSubjectPublicKeyInfo(certificateDer);
        if (spkiResult.IsFailure)
            return Result<ProviderSignature>.Failure(spkiResult.Error);

        return Result<ProviderSignature>.Success(new ProviderSignature
        {
            SubjectPublicKeyInfo = spkiResult.Value,
            Signature = normalized.Value,
            KeyId = _config.KeyId
        });
    }

    /// <summary>Parses the signer certificate and exports its SubjectPublicKeyInfo for embedding + pinning.</summary>
    private static Result<byte[]> ExtractSubjectPublicKeyInfo(byte[] certificateDer)
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(certificateDer);
            return certificate.PublicKey.ExportSubjectPublicKeyInfo();
        }
        catch (CryptographicException ex)
        {
            return Result<byte[]>.Failure(ErrorKind.SecurityError,
                $"SGN023: SignServer signerCertificate could not be parsed: {ex.Message}");
        }
    }

    private void ApplyAuthentication(HttpRequestMessage request)
    {
        switch (_config.AuthMode)
        {
            case SignServerAuthMode.Bearer when !string.IsNullOrEmpty(_config.BearerToken):
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.BearerToken);
                break;
            case SignServerAuthMode.Basic when !string.IsNullOrEmpty(_config.BasicUsername):
                var credential = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_config.BasicUsername}:{_config.BasicPassword}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
                break;
            case SignServerAuthMode.ClientCert:
            case SignServerAuthMode.None:
            default:
                // mTLS is enforced at the handler (TLS handshake); NOAUTH sends no header.
                break;
        }
    }

    /// <summary>
    /// Builds the handler the provider owns when none is injected, attaching the client certificate for
    /// mTLS and enabling certificate-revocation checking. <c>internal</c> so the mTLS wiring is unit-testable.
    /// </summary>
    internal static HttpClientHandler CreateDefaultHandler(SignServerConfig config)
    {
        var handler = new HttpClientHandler { CheckCertificateRevocationList = true };
        if (config.AuthMode == SignServerAuthMode.ClientCert && config.ClientCertificate is not null)
        {
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(config.ClientCertificate);
        }
        return handler;
    }

    private static Uri BuildProcessUri(SignServerConfig config)
    {
        var trimmed = config.BaseUrl.TrimEnd('/');
        return new Uri($"{trimmed}/signserver/rest/v1/workers/{Uri.EscapeDataString(config.Worker)}/process");
    }

    /// <summary>Disposes the owned <see cref="HttpClient"/>; an injected handler is left for its owner.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
