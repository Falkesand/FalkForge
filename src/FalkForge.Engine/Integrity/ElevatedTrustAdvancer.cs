namespace FalkForge.Engine.Integrity;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// Engine-side sender for the elevated <c>TrustStateAdvance</c> command (C16). After a fully-verified update
/// apply the engine records the accepted anti-downgrade epoch + revocations, but it cannot write the store
/// itself: the store directory is ACL-hardened (SYSTEM/Admins-write only) so a non-elevated write is denied.
/// This forwards the values to the elevated companion (which re-validates and persists them under the ACL)
/// over the established <see cref="IElevatedCommandGateway"/>.
/// </summary>
internal static class ElevatedTrustAdvancer
{
    /// <summary>
    /// Sends the accepted <paramref name="epoch"/> + <paramref name="revoked"/> to the elevated companion to
    /// be persisted. Returns the elevated command's result — a failure here means the store did NOT advance
    /// and must be surfaced loudly, never swallowed.
    /// </summary>
    public static async Task<Result<Unit>> AdvanceAsync(
        IElevatedCommandGateway gateway,
        int epoch,
        IReadOnlyList<string> revoked,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(revoked);

        var payload = TrustAdvancePayload.Serialize(epoch, revoked);
        var sent = await gateway.SendCommandAsync("TrustStateAdvance", payload, progress: null, ct)
            .ConfigureAwait(false);

        return sent.IsSuccess
            ? Result<Unit>.Success(Unit.Value)
            : Result<Unit>.Failure(sent.Error);
    }
}
