using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Cabinets;

/// <summary>
///     Strategy for placing a built cabinet file at its final destination. The
///     <see cref="CabinetBuilder" /> always writes cabs to a scratch directory;
///     the sink then either embeds the cab as an MSI <c>_Streams</c> entry or
///     copies it next to the MSI on disk. Keeping the two destinations behind a
///     single interface means the compiler pipeline does not branch on media
///     template flags and future destinations (e.g., a bundle payload stream)
///     can plug in without touching the pipeline.
/// </summary>
[SupportedOSPlatform("windows")]
public interface ICabinetSink
{
    /// <summary>
    ///     Place the cabinet produced at <paramref name="sourceCabPath" /> using
    ///     the logical <paramref name="cabinetFileName" /> planned for the disk.
    ///     Implementations must treat <paramref name="cabinetFileName" /> as
    ///     untrusted input: external file sinks in particular must reject path
    ///     separators, <c>..</c>, and absolute paths.
    /// </summary>
    Result<Unit> Place(string sourceCabPath, string cabinetFileName);
}
