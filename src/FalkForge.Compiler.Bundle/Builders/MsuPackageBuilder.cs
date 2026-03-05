namespace FalkForge.Compiler.Bundle.Builders;

public sealed class MsuPackageBuilder
{
    private readonly string _sourcePath;
    private string _displayName;
    private string _id;
    private string? _installCondition;
    private string? _kbArticle;
    private bool _vital = true;

    internal MsuPackageBuilder(string sourcePath)
    {
        _sourcePath = sourcePath;
        _id = Path.GetFileNameWithoutExtension(sourcePath);
        _displayName = _id;
    }

    public MsuPackageBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public MsuPackageBuilder DisplayName(string name)
    {
        _displayName = name;
        return this;
    }

    public MsuPackageBuilder Vital(bool vital)
    {
        _vital = vital;
        return this;
    }

    public MsuPackageBuilder KbArticle(string kbArticle)
    {
        _kbArticle = kbArticle;
        return this;
    }

    public MsuPackageBuilder InstallCondition(string condition)
    {
        _installCondition = condition;
        return this;
    }

    public MsuPackageBuilder InstallCondition(Condition condition)
    {
        return InstallCondition(condition.ToString());
    }

    internal BundlePackageModel Build()
    {
        return new BundlePackageModel
        {
            Id = _id,
            Type = BundlePackageType.MsuPackage,
            DisplayName = _displayName,
            Vital = _vital,
            SourcePath = _sourcePath,
            KbArticle = _kbArticle,
            InstallCondition = _installCondition
        };
    }
}