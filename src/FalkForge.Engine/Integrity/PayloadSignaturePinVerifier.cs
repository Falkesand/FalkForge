namespace FalkForge.Engine.Integrity;

using FalkForge.Platform.Windows;

/// <summary>
/// Centralizes the fail-closed enforcement of a payload's Authenticode publisher pin
/// (<c>AuthenticodeThumbprint</c> and/or <c>RemotePayloadCertificatePublicKey</c>) so every
/// payload-verification chokepoint (package cache, offline layout download) applies identical
/// semantics: when a pin is authored, the payload MUST be validly signed by the pinned publisher.
/// <para>
/// Fail-closed rule: if any pin is set but no <see cref="IAuthenticodeValidator"/> is available
/// (e.g. a non-Windows engine build), verification cannot be performed and the payload is rejected
/// rather than silently trusted. This turns "cannot verify" into "do not install", never into a
/// bypass.
/// </para>
/// </summary>
internal static class PayloadSignaturePinVerifier
{
    /// <summary>
    /// Verifies the signature pins for <paramref name="filePath"/>. Returns success when no pin is
    /// requested. When a pin is requested, requires a validator and a matching valid signature.
    /// </summary>
    /// <param name="validator">The platform Authenticode validator, or null when unavailable.</param>
    /// <param name="filePath">The exact file that will be used/installed (verified in place to avoid TOCTOU).</param>
    /// <param name="expectedThumbprint">Optional SHA-1 certificate thumbprint pin.</param>
    /// <param name="expectedPublicKeyHash">Optional SHA-256 SubjectPublicKeyInfo public-key pin.</param>
    /// <param name="packageId">Package identifier, used only for a clear failure message.</param>
    public static Result<Unit> Verify(
        IAuthenticodeValidator? validator,
        string filePath,
        string? expectedThumbprint,
        string? expectedPublicKeyHash,
        string packageId)
    {
        if (expectedThumbprint is null && expectedPublicKeyHash is null)
            return Unit.Value;

        if (validator is null)
        {
            return Result<Unit>.Failure(
                ErrorKind.SecurityError,
                $"Package '{packageId}' pins an Authenticode publisher but no signature validator is " +
                "available on this platform; refusing to install an unverifiable payload.");
        }

        return validator.ValidateSignature(filePath, expectedThumbprint, expectedPublicKeyHash);
    }
}
