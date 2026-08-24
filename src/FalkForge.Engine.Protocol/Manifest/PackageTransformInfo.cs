namespace FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// One MSI transform (.mst) an author declared for a package (D36). The transform's bytes are embedded
/// as a signed bundle payload keyed by <see cref="Id"/>, and this record carries the id together with
/// the payload's <see cref="Sha256Hash"/> so the runtime integrity gate can bind the signed transform
/// entry to the manifest-declared hash — the same binding it performs for an installable package.
/// <para>
/// The transform is <b>not</b> an installable package: it never appears in
/// <see cref="InstallerManifest.Packages"/>, so the plan and install paths (which key off that list)
/// never treat a transform id as something to run. It lives only under the owning package's
/// <see cref="PackageInfo.Transforms"/>.
/// </para>
/// </summary>
public sealed class PackageTransformInfo
{
    /// <summary>The author-chosen id of the transform. Matches the signed payload entry's name.</summary>
    public required string Id { get; init; }

    /// <summary>SHA-256 (hex) of the transform's (.mst) bytes, the value the signed entry must match.</summary>
    public required string Sha256Hash { get; init; }
}
