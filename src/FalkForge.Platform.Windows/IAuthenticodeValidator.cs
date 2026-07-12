namespace FalkForge.Platform.Windows;

public interface IAuthenticodeValidator
{
    /// <summary>
    /// Verifies a file's Authenticode signature and optionally pins the publisher identity.
    /// Trust (WinVerifyTrust) is always established first; the optional pins layer on top of —
    /// never replace — a successful trust check.
    /// </summary>
    /// <param name="filePath">Path to the file whose embedded Authenticode signature is verified.</param>
    /// <param name="expectedThumbprint">
    /// Optional SHA-1 certificate thumbprint (hex). When non-null, the signer certificate's
    /// thumbprint must match exactly. Pins the whole certificate (breaks on reissuance).
    /// </param>
    /// <param name="expectedPublicKeyHash">
    /// Optional SHA-256 hash (hex) of the signer certificate's SubjectPublicKeyInfo. When non-null,
    /// the signer's public key must hash to this value. Survives certificate rotation with the same
    /// key pair. Both pins, when supplied, must match.
    /// </param>
    Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint, string? expectedPublicKeyHash);
}
