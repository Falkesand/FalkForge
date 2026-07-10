namespace FalkForge.Engine.Elevation.Commands;

using FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// Whitelisted elevated command that advances the per-machine anti-downgrade/revocation store (C16). The
/// non-elevated engine cannot write under the restrictive store ACL, so after a fully-verified update apply
/// it sends the accepted epoch + revocations here (<see cref="TrustAdvancePayload"/>); this command — running
/// elevated — re-validates them, re-hardens the store directory (closing an anti-squat pre-create with a
/// loose ACL), and writes the store monotonically.
///
/// <para>The engine is the trusted caller on this pipe (HMAC + PID-verified), but the payload is still
/// re-validated here: a malformed blob is rejected, and monotonicity is enforced by
/// <see cref="TrustStateStore.Advance"/> (it never lowers the stored epoch and only unions revocations), so
/// a stale/replayed lower epoch cannot roll the store back even if one were sent.</para>
/// </summary>
public sealed class TrustStateAdvanceCommand : IElevatedCommand
{
    private readonly string _storePath;

    /// <summary>Production ctor: writes the per-machine default store path.</summary>
    public TrustStateAdvanceCommand()
        : this(TrustStateStore.DefaultPath)
    {
    }

    /// <summary>Test/override ctor: writes the supplied store path.</summary>
    public TrustStateAdvanceCommand(string storePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(storePath);
        _storePath = storePath;
    }

    public string Name => "TrustStateAdvance";

    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!TrustAdvancePayload.TryDeserialize(payload, out var epoch, out var revoked))
            return Result<byte[]>.Failure(ErrorKind.SecurityError,
                "TrustStateAdvance: malformed advance payload; refusing to touch the trust store.");

        // Re-harden the store directory before writing: create it hardened when absent, or reset a
        // non-conforming (attacker pre-created / loose) directory to the restrictive DACL (anti-squat).
        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir))
        {
            var secured = TrustStateStore.EnsureSecuredDirectory(dir);
            if (secured.IsFailure)
                return Result<byte[]>.Failure(secured.Error);
        }

        // Monotonic + union — never lowers the epoch, so a stale/replayed lower epoch is a no-op, not a
        // rollback. Fails loud (a Result failure) if the elevated write cannot land.
        var advance = TrustStateStore.Advance(_storePath, epoch, revoked);
        if (advance.IsFailure)
            return Result<byte[]>.Failure(advance.Error);

        return Array.Empty<byte>();
    }
}
