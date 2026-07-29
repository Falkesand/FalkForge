namespace FalkForge.Sbom;

public sealed class SbomOptions
{
    private readonly List<SbomComponent> _additionalComponents = [];

    public IReadOnlyList<SbomComponent> AdditionalComponents => _additionalComponents;

    /// <param name="sha1">
    /// Optional SHA-1 of the same bytes as <paramref name="sha256"/>. Only consulted for SPDX output
    /// of a <see cref="SbomComponentType.File"/> component, where SPDX 2.3 §8.4 makes a SHA1
    /// checksum mandatory — without it, a File component cannot appear in an SPDX document at all.
    /// Components of any other type become SPDX packages, whose checksums are optional, so they may
    /// leave it unset. CycloneDX output ignores it.
    /// </param>
    public SbomOptions AddComponent(string name, string version, SbomComponentType type, string sha256,
        string? publisher = null, string? sha1 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        _additionalComponents.Add(new SbomComponent
        {
            Name = name,
            Version = version,
            Type = type,
            Sha256Hash = sha256,
            Publisher = publisher,
            Sha1Hash = sha1
        });
        return this;
    }
}
