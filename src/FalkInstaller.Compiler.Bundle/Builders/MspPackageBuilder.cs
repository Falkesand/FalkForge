namespace FalkInstaller.Compiler.Bundle.Builders;

public sealed class MspPackageBuilder
{
    private readonly string _sourcePath;
    private string _id;
    private string _displayName;
    private bool _vital = true;
    private string? _patchCode;
    private string? _targetProductCode;
    private string? _installCondition;

    internal MspPackageBuilder(string sourcePath)
    {
        _sourcePath = sourcePath;
        _id = Path.GetFileNameWithoutExtension(sourcePath);
        _displayName = _id;
    }

    public MspPackageBuilder Id(string id) { _id = id; return this; }
    public MspPackageBuilder DisplayName(string name) { _displayName = name; return this; }
    public MspPackageBuilder Vital(bool vital) { _vital = vital; return this; }
    public MspPackageBuilder PatchCode(string patchCode) { _patchCode = patchCode; return this; }
    public MspPackageBuilder TargetProductCode(string targetProductCode) { _targetProductCode = targetProductCode; return this; }
    public MspPackageBuilder InstallCondition(string condition) { _installCondition = condition; return this; }

    internal BundlePackageModel Build()
    {
        return new BundlePackageModel
        {
            Id = _id,
            Type = BundlePackageType.MspPackage,
            DisplayName = _displayName,
            Vital = _vital,
            SourcePath = _sourcePath,
            PatchCode = _patchCode,
            TargetProductCode = _targetProductCode,
            InstallCondition = _installCondition
        };
    }
}
