namespace FalkForge.Models;

public sealed class IntegrityConfiguration
{
    public string? SigningKeyPath { get; init; }

    /// <summary>
    /// Optional PEM private-key paths for rotation-safe dual-sign: every listed key signs the
    /// identical payload-hash message, producing one signature entry each in the v2 envelope. When
    /// non-empty this supersedes <see cref="SigningKeyPath"/>. Empty/null falls back to the single
    /// <see cref="SigningKeyPath"/> (or an ephemeral key when that is also unset).
    /// </summary>
    public IReadOnlyList<string>? SigningKeyPaths { get; init; }
    public string? CertStoreThumbprint { get; init; }
    public string? StoreLocation { get; init; }
    public string? VaultProvider { get; init; }
    public string? VaultKeyRef { get; init; }
    public SbomFormat SbomFormat { get; init; } = SbomFormat.Spdx;
}
