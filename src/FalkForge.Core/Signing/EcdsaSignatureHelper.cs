using System.Security.Cryptography;

namespace FalkForge.Signing;

/// <summary>
/// Shared ECDSA signing step for the built-in <see cref="ISignatureProvider"/> implementations. Centralizing
/// it keeps every local provider producing the byte-compatible result the verifier expects: a signature over
/// <c>SHA-256(message)</c> in IEEE P1363 (r‖s) encoding.
/// </summary>
internal static class EcdsaSignatureHelper
{
    /// <summary>
    /// Signs <paramref name="message"/> with <paramref name="key"/> and packages the result. The default
    /// <see cref="ECDsa.SignHash(byte[])"/> overload emits IEEE P1363 (r‖s) — the manifest's canonical
    /// encoding — so no explicit <see cref="DSASignatureFormat"/> is passed. The signature is then
    /// canonicalized to low-S (<see cref="EcdsaLowS"/>): CNG emits the malleable high-S form about half
    /// the time, and the verifier rejects it. The caller owns the key.
    /// </summary>
    internal static Result<ProviderSignature> Sign(ECDsa key, ReadOnlySpan<byte> message, string keyId)
    {
        var hash = SHA256.HashData(message);
        return Result<ProviderSignature>.Success(new ProviderSignature
        {
            SubjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo(),
            Signature = EcdsaLowS.Canonicalize(key.SignHash(hash)),
            KeyId = keyId
        });
    }
}
