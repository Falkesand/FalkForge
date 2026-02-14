namespace FalkInstaller.Compiler.Bundle.Builders;

public sealed class ChainBuilder
{
    private readonly List<BundlePackageModel> _packages = new();

    public ChainBuilder MsiPackage(string sourcePath, Action<BundlePackageBuilder> configure)
    {
        var builder = new BundlePackageBuilder(BundlePackageType.MsiPackage, sourcePath);
        configure(builder);
        _packages.Add(builder.Build());
        return this;
    }

    public ChainBuilder ExePackage(string sourcePath, Action<BundlePackageBuilder> configure)
    {
        var builder = new BundlePackageBuilder(BundlePackageType.ExePackage, sourcePath);
        configure(builder);
        _packages.Add(builder.Build());
        return this;
    }

    public ChainBuilder NetRuntime(string sourcePath, Action<BundlePackageBuilder> configure)
    {
        var builder = new BundlePackageBuilder(BundlePackageType.NetRuntime, sourcePath);
        configure(builder);
        _packages.Add(builder.Build());
        return this;
    }

    internal IReadOnlyList<BundlePackageModel> Build() => _packages.AsReadOnly();
}
