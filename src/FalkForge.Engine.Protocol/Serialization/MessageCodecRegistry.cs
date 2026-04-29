using System.Collections.Generic;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization.Codecs;

namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Static facade over the singleton <see cref="MessageCodecRegistryInstance"/> used by
/// the protocol layer. Delegates write-side and read-side resolution to the underlying
/// instance so callers do not need to thread a registry reference through the pipeline.
/// </summary>
/// <remarks>
/// This is the registration site. Real codecs will be added to <c>s_codecs</c> in phase 5+;
/// the array is intentionally empty during the registry stand-up phase so the rest of the
/// pipeline can evolve in parallel without temporarily breaking byte parity.
/// </remarks>
public static class MessageCodecRegistry
{
    // Registration site. Phase 5 codecs land one per feature commit.
    private static readonly IMessageCodec[] s_codecs =
    [
        CancelCodec.Instance,
        LogCodec.Instance,
        RequestDetectCodec.Instance,
        RequestPlanCodec.Instance,
        RequestApplyCodec.Instance,
        DetectBeginCodec.Instance,
        PlanBeginCodec.Instance,
        ApplyBeginCodec.Instance,
        PhaseChangedCodec.Instance,
        ErrorCodec.Instance,
        ProgressCodec.Instance,
        ShutdownRequestCodec.Instance,
        ShutdownResponseCodec.Instance,
        SetInstallDirectoryCodec.Instance,
        SetFeatureSelectionCodec.Instance,
        SetPropertyCodec.Instance,
        LicenseCodec.Instance,
    ];

    private static readonly MessageCodecRegistryInstance s_instance = new(s_codecs);

    /// <summary>All registered codecs.</summary>
    public static IReadOnlyCollection<IMessageCodec> All => s_instance.All;

    /// <summary>Resolves the write-side codec for the runtime type of <paramref name="message"/>.</summary>
    public static IMessageCodec ForWrite(EngineMessage message) => s_instance.ForWrite(message);

    /// <summary>
    /// Resolves the read-side codec for <paramref name="type"/> at <paramref name="wireVersion"/>,
    /// falling back to the highest registered version less than or equal to it.
    /// </summary>
    public static Result<IMessageCodec> ForRead(MessageType type, ushort wireVersion)
        => s_instance.ForRead(type, wireVersion);
}
