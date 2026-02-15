namespace FalkForge.Engine.Cache;

public sealed class CacheLayout
{
    private readonly string _basePath;

    public CacheLayout(InstallScope scope)
    {
        _basePath = scope == InstallScope.PerMachine
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FalkForge",
                "Cache")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FalkForge",
                "Cache");
    }

    public CacheLayout(string basePath)
    {
        _basePath = basePath;
    }

    public string BasePath => _basePath;

    public string GetBundlePath(Guid bundleId) =>
        Path.Combine(_basePath, bundleId.ToString("D"));

    public string GetPackagePath(Guid bundleId, string packageId) =>
        Path.Combine(GetBundlePath(bundleId), packageId);

    public string GetPayloadPath(Guid bundleId, string packageId, string fileName) =>
        Path.Combine(GetPackagePath(bundleId, packageId), fileName);
}
