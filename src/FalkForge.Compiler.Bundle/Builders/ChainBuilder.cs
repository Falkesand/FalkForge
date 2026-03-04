namespace FalkForge.Compiler.Bundle.Builders;

public sealed class ChainBuilder
{
    private readonly List<ChainItem> _chainItems = new();
    private readonly List<BundlePackageModel> _packages = new();

    public ChainBuilder MsiPackage(string sourcePath, Action<BundlePackageBuilder> configure)
    {
        var builder = new BundlePackageBuilder(BundlePackageType.MsiPackage, sourcePath);
        configure(builder);
        var package = builder.Build();
        _packages.Add(package);
        _chainItems.Add(new PackageChainItem(package));
        return this;
    }

    public ChainBuilder ExePackage(string sourcePath, Action<BundlePackageBuilder> configure)
    {
        var builder = new BundlePackageBuilder(BundlePackageType.ExePackage, sourcePath);
        configure(builder);
        var package = builder.Build();
        _packages.Add(package);
        _chainItems.Add(new PackageChainItem(package));
        return this;
    }

    public ChainBuilder NetRuntime(string sourcePath, Action<BundlePackageBuilder> configure)
    {
        var builder = new BundlePackageBuilder(BundlePackageType.NetRuntime, sourcePath);
        configure(builder);
        var package = builder.Build();
        _packages.Add(package);
        _chainItems.Add(new PackageChainItem(package));
        return this;
    }

    public ChainBuilder MsuPackage(string sourcePath, Action<MsuPackageBuilder> configure)
    {
        var builder = new MsuPackageBuilder(sourcePath);
        configure(builder);
        var package = builder.Build();
        _packages.Add(package);
        _chainItems.Add(new PackageChainItem(package));
        return this;
    }

    public ChainBuilder MspPackage(string sourcePath, Action<MspPackageBuilder> configure)
    {
        var builder = new MspPackageBuilder(sourcePath);
        configure(builder);
        var package = builder.Build();
        _packages.Add(package);
        _chainItems.Add(new PackageChainItem(package));
        return this;
    }

    public ChainBuilder BundlePackage(string sourcePath, Action<NestedBundlePackageBuilder> configure)
    {
        var builder = new NestedBundlePackageBuilder(sourcePath);
        configure(builder);
        var package = builder.Build();
        _packages.Add(package);
        _chainItems.Add(new PackageChainItem(package));
        return this;
    }

    public ChainBuilder RollbackBoundary(string id, Action<RollbackBoundaryBuilder>? configure = null)
    {
        var builder = new RollbackBoundaryBuilder().Id(id);
        configure?.Invoke(builder);
        _chainItems.Add(new RollbackBoundaryChainItem(builder.Build()));
        return this;
    }

    public ChainBuilder RollbackBoundary(RollbackBoundaryRef boundaryRef,
        Action<RollbackBoundaryBuilder>? configure = null)
    {
        return RollbackBoundary(boundaryRef.Id, configure);
    }

    internal IReadOnlyList<BundlePackageModel> Build()
    {
        return _packages.AsReadOnly();
    }

    internal IReadOnlyList<ChainItem> BuildChain()
    {
        return _chainItems.AsReadOnly();
    }
}