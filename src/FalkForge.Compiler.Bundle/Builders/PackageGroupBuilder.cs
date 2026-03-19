namespace FalkForge.Compiler.Bundle.Builders;

/// <summary>
/// Fluent builder for creating reusable <see cref="PackageGroupModel"/> instances.
/// Groups are flattened into the chain at build time.
/// </summary>
public sealed class PackageGroupBuilder
{
    private string _id = string.Empty;
    private readonly List<BundlePackageModel> _packages = new();

    public PackageGroupBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public PackageGroupBuilder ExePackage(string sourcePath, Action<BundlePackageBuilder> configure)
    {
        var builder = new BundlePackageBuilder(BundlePackageType.ExePackage, sourcePath);
        configure(builder);
        _packages.Add(builder.Build());
        return this;
    }

    public PackageGroupBuilder MsiPackage(string sourcePath, Action<BundlePackageBuilder> configure)
    {
        var builder = new BundlePackageBuilder(BundlePackageType.MsiPackage, sourcePath);
        configure(builder);
        _packages.Add(builder.Build());
        return this;
    }

    public PackageGroupModel Build()
    {
        return new PackageGroupModel
        {
            Id = _id,
            Packages = _packages.AsReadOnly()
        };
    }
}
