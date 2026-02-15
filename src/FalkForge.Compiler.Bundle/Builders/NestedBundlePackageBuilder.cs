namespace FalkForge.Compiler.Bundle.Builders;

public sealed class NestedBundlePackageBuilder
{
    private readonly string _sourcePath;
    private string _id;
    private string _displayName;
    private bool _vital = true;
    private string? _installCondition;

    internal NestedBundlePackageBuilder(string sourcePath)
    {
        _sourcePath = sourcePath;
        _id = Path.GetFileNameWithoutExtension(sourcePath);
        _displayName = _id;
    }

    public NestedBundlePackageBuilder Id(string id) { _id = id; return this; }
    public NestedBundlePackageBuilder DisplayName(string name) { _displayName = name; return this; }
    public NestedBundlePackageBuilder Vital(bool vital) { _vital = vital; return this; }
    public NestedBundlePackageBuilder InstallCondition(string condition) { _installCondition = condition; return this; }

    internal BundlePackageModel Build()
    {
        return new BundlePackageModel
        {
            Id = _id,
            Type = BundlePackageType.BundlePackage,
            DisplayName = _displayName,
            Vital = _vital,
            SourcePath = _sourcePath,
            InstallCondition = _installCondition
        };
    }
}
