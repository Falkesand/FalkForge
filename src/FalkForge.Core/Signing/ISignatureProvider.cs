namespace FalkForge.Signing;

/// <summary>
/// A pluggable backend that produces one ECDSA-P256 signature over the canonical manifest message.
///
/// <para>This is the seam that lets the integrity signer use different signing backends — the built-in
/// local-PEM key (<see cref="PemSignatureProvider"/>), the zero-config ephemeral key
/// (<see cref="EphemeralSignatureProvider"/>), or a future remote signing service — behind one contract.
/// The abstraction is asynchronous because a remote backend performs network I/O; the built-in
/// providers complete synchronously and never block.</para>
///
/// <para><b>Contract.</b> The provider is handed the <i>canonical message bytes</i> exactly as computed by
/// <c>IntegrityEnvelopeCodec.ComputeSignedBytes</c> (the UTF-8 JSON of the file list, plus epoch/revocation
/// binding when present). It is responsible for the standard ECDSA step: hash the message with SHA-256 and
/// sign that hash, returning the signature in IEEE P1363 (r‖s) encoding — see
/// <see cref="ProviderSignature"/> for why the encoding is fixed. Returning a signature over anything other
/// than <c>SHA-256(message)</c>, or in any other encoding, makes the envelope fail verification.</para>
/// </summary>
public interface ISignatureProvider
{
    /// <summary>
    /// Signs the supplied canonical message and returns the signature plus the public key it was produced
    /// with. Returns a failed <see cref="Result{T}"/> (never throws for expected failures such as a missing
    /// key file) so the caller can surface a typed integrity error.
    /// </summary>
    /// <param name="message">
    /// The canonical bytes to sign — the output of <c>IntegrityEnvelopeCodec.ComputeSignedBytes</c>. The
    /// provider hashes these with SHA-256 and signs the hash.
    /// </param>
    /// <param name="cancellationToken">Cancellation for remote backends; ignored by synchronous providers.</param>
    ValueTask<Result<ProviderSignature>> SignAsync(
        ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);
}
