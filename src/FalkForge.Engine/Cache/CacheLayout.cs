namespace FalkForge.Engine.Cache;

using System.Text.RegularExpressions;

public sealed partial class CacheLayout
{
    private static readonly Regex ValidPackageIdPattern = GetValidPackageIdRegex();

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

    public string GetPackagePath(Guid bundleId, string packageId)
    {
        ValidatePackageId(packageId);
        return Path.Combine(GetBundlePath(bundleId), packageId);
    }

    public string GetPayloadPath(Guid bundleId, string packageId, string fileName)
    {
        ValidatePackageId(packageId);
        var sanitizedFileName = SanitizeFileName(fileName);

        var path = Path.Combine(GetBundlePath(bundleId), packageId, sanitizedFileName);
        var resolvedPath = Path.GetFullPath(path);
        var resolvedBase = Path.GetFullPath(GetBundlePath(bundleId) + Path.DirectorySeparatorChar);

        if (!resolvedPath.StartsWith(resolvedBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Payload path escapes the cache directory. File name '{fileName}' resolves outside the bundle cache.",
                nameof(fileName));
        }

        return resolvedPath;
    }

    private static void ValidatePackageId(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        if (!ValidPackageIdPattern.IsMatch(packageId))
        {
            throw new ArgumentException(
                $"Package ID '{packageId}' contains invalid characters. Only alphanumeric characters, dots, hyphens, and underscores are allowed.",
                nameof(packageId));
        }

        // Reject relative path references that pass the character allowlist
        if (packageId is "." or "..")
        {
            throw new ArgumentException(
                $"Package ID '{packageId}' contains invalid characters. Only alphanumeric characters, dots, hyphens, and underscores are allowed.",
                nameof(packageId));
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // Strip any directory components to prevent path traversal
        var sanitized = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException(
                $"File name '{fileName}' does not contain a valid file name component.",
                nameof(fileName));
        }

        return sanitized;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._\-]+$")]
    private static partial Regex GetValidPackageIdRegex();
}
