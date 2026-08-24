namespace FalkForge.Engine.Integrity;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// Engine-side sender for the elevated <c>TrustStateAdvance</c> command (C16). After a fully-verified update
/// apply the engine needs the accepted anti-downgrade epoch + revocations recorded, but it cannot write the
/// store itself: the store directory is ACL-hardened (SYSTEM/Admins-write only) so a non-elevated write is
/// denied. It forwards the accepted update's publisher-signed manifest to the elevated companion, which
/// re-verifies it against its OWN baked key set and takes the epoch + revocations from the verified envelope
/// (never from the engine), then persists them under the ACL over the established
/// <see cref="IElevatedCommandGateway"/>.
/// </summary>
internal static class ElevatedTrustAdvancer
{
    /// <summary>
    /// Sends the accepted update's publisher-signed <paramref name="manifestJson"/> to the elevated companion
    /// to be verified and persisted. Returns the elevated command's result — a failure here means the store
    /// did NOT advance and must be surfaced loudly, never swallowed.
    /// </summary>
    public static async Task<Result<Unit>> AdvanceAsync(
        IElevatedCommandGateway gateway,
        string manifestJson,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(manifestJson);

        var payload = TrustAdvancePayload.Serialize(manifestJson);
        var sent = await gateway.SendCommandAsync("TrustStateAdvance", payload, progress: null, ct)
            .ConfigureAwait(false);

        return sent.IsSuccess
            ? Result<Unit>.Success(Unit.Value)
            : Result<Unit>.Failure(sent.Error);
    }
}
