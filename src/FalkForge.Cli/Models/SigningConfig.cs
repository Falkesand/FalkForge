using System.Text.Json;
using System.Text.Json.Serialization;

namespace FalkForge.Cli.Models;

/// <summary>
/// Optional <c>signing</c> section of the forge JSON config: selects the bundle-integrity
/// signature backend (the C17 <c>ISignatureProvider</c> seam) for <c>forge build</c>.
///
/// <para><b>No secrets in this file — ever.</b> The PEM key is referenced by file path
/// (<see cref="KeyPath"/>) or environment variable name (<see cref="KeyEnv"/>); all SignServer
/// auth material is referenced by environment variable NAME (<c>*Env</c> fields) and read from
/// the environment at build time. Validation rejects inline secret material (JSN016).</para>
/// </summary>
public sealed class SigningConfig
{
    /// <summary>Signature backend: <c>none</c> (explicit off), <c>pem</c>, or <c>signserver</c>.</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>PEM provider: path to the ECDSA private-key PEM file (relative paths resolve against the config file).</summary>
    [JsonPropertyName("keyPath")]
    public string? KeyPath { get; set; }

    /// <summary>PEM provider: NAME of an environment variable whose VALUE is the private-key PEM.</summary>
    [JsonPropertyName("keyEnv")]
    public string? KeyEnv { get; set; }

    /// <summary>
    /// PEM provider, hybrid post-quantum signing: path to the ML-DSA (FIPS 204) companion
    /// private-key PEM. Present ⇒ the bundle is HYBRID-signed — the classical key and the ML-DSA
    /// key both sign the same manifest message (PQ-hybrid design §2.2). Same secret rules as
    /// <see cref="KeyPath"/>: a file path, never inline key material.
    /// </summary>
    [JsonPropertyName("pqKeyPath")]
    public string? PqKeyPath { get; set; }

    /// <summary>
    /// PEM provider, hybrid post-quantum signing: NAME of an environment variable whose VALUE is
    /// the ML-DSA companion private-key PEM (see <see cref="PqKeyPath"/>).
    /// </summary>
    [JsonPropertyName("pqKeyEnv")]
    public string? PqKeyEnv { get; set; }

    /// <summary>SignServer: base URL of the instance, e.g. <c>https://signserver.example.com:8443</c>.</summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    /// <summary>SignServer: PlainSigner worker name or numeric id.</summary>
    [JsonPropertyName("worker")]
    public string? Worker { get; set; }

    /// <summary>SignServer auth mode: <c>none</c> (default), <c>basic</c>, <c>bearer</c>, or <c>clientcert</c>.</summary>
    [JsonPropertyName("authMode")]
    public string? AuthMode { get; set; }

    /// <summary>SignServer bearer auth: NAME of the environment variable holding the bearer token.</summary>
    [JsonPropertyName("bearerTokenEnv")]
    public string? BearerTokenEnv { get; set; }

    /// <summary>SignServer basic auth: NAME of the environment variable holding the username.</summary>
    [JsonPropertyName("usernameEnv")]
    public string? UsernameEnv { get; set; }

    /// <summary>SignServer basic auth: NAME of the environment variable holding the password.</summary>
    [JsonPropertyName("passwordEnv")]
    public string? PasswordEnv { get; set; }

    /// <summary>SignServer mTLS: NAME of the environment variable holding the client-certificate PFX path.</summary>
    [JsonPropertyName("clientCertPathEnv")]
    public string? ClientCertPathEnv { get; set; }

    /// <summary>SignServer mTLS: NAME of the environment variable holding the PFX password (omit for a passwordless PFX).</summary>
    [JsonPropertyName("clientCertPasswordEnv")]
    public string? ClientCertPasswordEnv { get; set; }

    /// <summary>Optional operator-facing key label copied into the envelope's <c>keyId</c> (informational only).</summary>
    [JsonPropertyName("keyId")]
    public string? KeyId { get; set; }

    /// <summary>
    /// Captures JSON keys that do not match any recognized field. The signing section fails
    /// closed on unknown keys (JSN016): a typo must not silently disable authentication, and a
    /// pasted secret (e.g. <c>"bearerToken"</c>) must be rejected, not ignored.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; set; }
}
