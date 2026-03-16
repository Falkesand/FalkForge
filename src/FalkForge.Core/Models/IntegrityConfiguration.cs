namespace FalkForge.Models;

public sealed class IntegrityConfiguration
{
    public string? SigningKeyPath { get; init; }
    public string? CertStoreThumbprint { get; init; }
    public string? StoreLocation { get; init; }
    public string? VaultProvider { get; init; }
    public string? VaultKeyRef { get; init; }
    public SbomFormat SbomFormat { get; init; } = SbomFormat.Spdx;
}
