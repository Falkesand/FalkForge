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
/// <para>
/// <strong>Registration site.</strong> All codecs are registered here; each
/// <see cref="IMessageCodec"/> declares its own <c>WireVersion</c> so per-message
/// versioning is self-describing rather than controlled by a single global constant.
/// </para>
/// <para>
/// <strong>Single-version wire contract — cross-version interop is not supported.</strong>
/// FalkForge ships UI, Engine, and Elevation as a single atomic bundle from the same
/// source tree; a bundle update replaces all three processes together. The registry
/// therefore only needs to track the <em>current</em> codec for each message type.
/// Old v1 codecs for messages that have since moved to v2 (e.g.
/// <c>LogMessage</c> and <c>PhaseChangedMessage</c>) are intentionally absent — they
/// would never be reached in production because the peer process is always at the same
/// version. If cross-version interop ever becomes a requirement it must be designed
/// explicitly and is out of scope for the current release.
/// </para>
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
        DetectCompleteCodec.Instance,
        PlanBeginCodec.Instance,
        PlanCompleteCodec.Instance,
        ApplyBeginCodec.Instance,
        ApplyCompleteCodec.Instance,
        PhaseChangedCodec.Instance,
        ErrorCodec.Instance,
        ProgressCodec.Instance,
        ShutdownRequestCodec.Instance,
        ShutdownResponseCodec.Instance,
        SetInstallDirectoryCodec.Instance,
        SetFeatureSelectionCodec.Instance,
        SetPropertyCodec.Instance,
        SetSecurePropertyCodec.Instance,
        LicenseCodec.Instance,
        LaunchUpdateCodec.Instance,
        UpdateAvailableCodec.Instance,
        UpdateReadyCodec.Instance,
        UpdateDownloadProgressCodec.Instance,
        ElevateExecuteCodec.Instance,
        ElevateProgressCodec.Instance,
        ElevateResultCodec.Instance,
        SessionStartCodec.Instance,
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
