namespace FalkForge.Sbom;

public sealed class SbomOptions
{
    private readonly List<SbomComponent> _additionalComponents = [];

    public IReadOnlyList<SbomComponent> AdditionalComponents => _additionalComponents;

    public SbomOptions AddComponent(string name, string version, SbomComponentType type, string sha256,
        string? publisher = null)
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
            Publisher = publisher
        });
        return this;
    }
}
