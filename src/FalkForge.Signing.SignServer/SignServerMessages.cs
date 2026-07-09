using System.Text.Json.Serialization;

namespace FalkForge.Signing.SignServer;

/// <summary>The SignServer REST <c>/process</c> request body: base64-encoded canonical message bytes.</summary>
internal sealed class SignServerProcessRequest
{
    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "BASE64";

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
}

/// <summary>
/// The SignServer REST <c>/process</c> response: <c>data</c> is the base64 signature (DER for
/// SHA256withECDSA), <c>signerCertificate</c> is the base64-DER X.509 signer certificate.
/// </summary>
internal sealed class SignServerProcessResponse
{
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("signerCertificate")]
    public string? SignerCertificate { get; set; }
}

[JsonSerializable(typeof(SignServerProcessRequest))]
[JsonSerializable(typeof(SignServerProcessResponse))]
internal sealed partial class SignServerJsonContext : JsonSerializerContext;
