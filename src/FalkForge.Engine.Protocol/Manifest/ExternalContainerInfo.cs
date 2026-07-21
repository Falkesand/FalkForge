namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Describes an external, separately-downloadable payload container (A6). Payloads authored into a
/// container that carries a <c>DownloadUrl</c> are NOT embedded in the self-extracting bundle exe;
/// instead they are packaged into a standalone container file (the same FALKBUNDLE payload-embed
/// format, minus an engine stub) that the publisher hosts at <see cref="DownloadUrl"/>. At runtime the
/// engine downloads that container, verifies it, extracts its payloads into the same cache directory
/// the resolved-path install chain reads from, and installs them exactly like embedded payloads.
/// <para>
/// <b>Integrity.</b> <see cref="Sha256"/> is the SHA-256 (hex) of the whole container file, verified
/// against the downloaded bytes before the container is opened — a mismatch fails loud and the
/// container is never used. For a <em>signed</em> bundle this whole-file hash is only the transport
/// check: the authoritative trust is that every payload the container yields is bound back to the
/// ECDSA-signed manifest set (<c>SignedPayloadTocVerifier</c>), so a re-hosted or rebuilt container
/// whose payloads were not signed by the trusted publisher is rejected before extraction. For an
/// unsigned bundle the whole-file hash plus each payload's own container-TOC hash provide the same
/// tamper detection an unsigned embedded bundle has.
/// </para>
/// <para>
/// Additive and defaulted: older engines that predate this field skip it on deserialization and see a
/// bundle with no external containers (all payloads embedded), so the field is backward compatible in
/// both directions.
/// </para>
/// </summary>
public sealed record ExternalContainerInfo
{
    /// <summary>Container id, matching the <c>ContainerId</c> the member packages carry.</summary>
    public required string Id { get; init; }

    /// <summary>HTTPS URL the publisher hosts the container file at; the engine downloads from here.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>SHA-256 (upper-case hex) of the whole container file, verified on download.</summary>
    public required string Sha256 { get; init; }

    /// <summary>
    /// The build-produced container artifact file name (sits next to the bundle exe in the build
    /// output). The publisher uploads this file to <see cref="DownloadUrl"/>; the engine identifies
    /// the download purely by URL, so this is provenance/tooling metadata, not a runtime path.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The package ids the container carries. The engine cross-checks the container's own table of
    /// contents against this declared membership so a container that omits or adds payloads relative
    /// to what the manifest promised is rejected (defense in depth on top of the signed-set binding).
    /// </summary>
    public required string[] PackageIds { get; init; }
}
