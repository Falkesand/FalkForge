using System.Text;
using FalkForge.Models;

namespace FalkForge.WinGet;

/// <summary>
/// Emits the 3-file WinGet manifest set (version, installer, defaultLocale) as YAML.
/// </summary>
public static class WinGetManifestWriter
{
    /// <summary>
    /// Writes the WinGet manifest files to the specified output directory.
    /// Creates the directory structure: {outputDir}/{first-letter}/{Publisher}/{PackageName}/{version}/
    /// </summary>
    public static Result<string> Write(
        PackageModel package,
        WinGetConfig config,
        string outputDir,
        string installerSha256,
        string installerFileName)
    {
        if (string.IsNullOrWhiteSpace(config.PackageIdentifier))
            return Result<string>.Failure(ErrorKind.Validation, "WinGet PackageIdentifier is required.");

        if (!config.PackageIdentifier.Contains('.'))
            return Result<string>.Failure(ErrorKind.Validation,
                "WinGet PackageIdentifier must be in Publisher.PackageName format (e.g., Contoso.MyApp).");

        var id = config.PackageIdentifier;
        var version = package.Version?.ToString(3) ?? "0.0.0";

        // Build the standard winget-pkgs directory structure
        var idParts = id.Split('.');
        var manifestDir = Path.Combine(
            outputDir,
            idParts[0][..1].ToLowerInvariant(),
            Path.Combine(idParts),
            version);

        if (config.InstallerUrl is null)
            return Result<string>.Failure(ErrorKind.Validation,
                "WGT002: InstallerUrl is required. Set WinGetConfig.InstallerUrl before generating manifests.");

        Directory.CreateDirectory(manifestDir);

        WriteVersionManifest(manifestDir, id, version, config.ManifestVersion);
        WriteInstallerManifest(manifestDir, id, version, package, config, installerSha256);
        WriteLocaleManifest(manifestDir, id, version, package, config);

        // Additional locale manifests (one per extra locale entry).
        if (config.Locales is { Length: > 0 })
        {
            foreach (var locale in config.Locales)
                WriteExtraLocaleManifest(manifestDir, id, version, locale, config.ManifestVersion);
        }

        return manifestDir;
    }

    private static void WriteVersionManifest(string dir, string id, string version, string manifestVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PackageIdentifier: {id}");
        sb.AppendLine($"PackageVersion: {version}");
        sb.AppendLine("DefaultLocale: en-US");
        sb.AppendLine("ManifestType: version");
        sb.AppendLine($"ManifestVersion: {manifestVersion}");

        var path = Path.Combine(dir, $"{id}.yaml");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteInstallerManifest(
        string dir,
        string id,
        string version,
        PackageModel package,
        WinGetConfig config,
        string installerSha256)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PackageIdentifier: {id}");
        sb.AppendLine($"PackageVersion: {version}");
        sb.AppendLine("Installers:");
        sb.AppendLine($"- Architecture: {MapArchitecture(package.Architecture)}");
        sb.AppendLine($"  InstallerUrl: {config.InstallerUrl}");
        sb.AppendLine($"  InstallerSha256: {installerSha256}");
        sb.AppendLine($"  InstallerType: {MapInstallerType(config.InstallerType)}");

        // EXE bundles need silent-install switches; MSI installers rely on ProductCode/UpgradeBehavior.
        if (config.InstallerType == WinGetInstallerType.Exe)
        {
            sb.AppendLine("  InstallerSwitches:");
            sb.AppendLine("    Silent: /quiet");
            sb.AppendLine("    SilentWithProgress: /passive");
        }

        sb.AppendLine($"  Scope: {MapScope(package.Scope)}");

        if (config.InstallerType == WinGetInstallerType.Msi)
            sb.AppendLine($"  ProductCode: \"{{{package.ProductCode}}}\"");

        sb.AppendLine("  UpgradeBehavior: install");
        sb.AppendLine("ManifestType: installer");
        sb.AppendLine($"ManifestVersion: {config.ManifestVersion}");

        var path = Path.Combine(dir, $"{id}.installer.yaml");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteLocaleManifest(
        string dir,
        string id,
        string version,
        PackageModel package,
        WinGetConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PackageIdentifier: {id}");
        sb.AppendLine($"PackageVersion: {version}");
        sb.AppendLine("PackageLocale: en-US");
        sb.AppendLine($"Publisher: {package.Manufacturer}");
        sb.AppendLine($"PackageName: {package.Name}");
        sb.AppendLine($"License: {config.License}");
        sb.AppendLine($"ShortDescription: {config.ShortDescription}");

        if (config.Moniker is not null)
            sb.AppendLine($"Moniker: {config.Moniker}");

        if (config.Tags is { Length: > 0 })
        {
            sb.AppendLine("Tags:");
            foreach (var tag in config.Tags)
                sb.AppendLine($"- {tag}");
        }

        if (package.Description is not null)
            sb.AppendLine($"Description: {package.Description}");

        if (package.AboutUrl is not null)
            sb.AppendLine($"PackageUrl: {package.AboutUrl}");

        if (config.PrivacyUrl is not null)
            sb.AppendLine($"PrivacyUrl: {config.PrivacyUrl}");

        if (config.ReleaseNotes is not null)
            sb.AppendLine($"ReleaseNotes: {config.ReleaseNotes}");

        if (config.ReleaseNotesUrl is not null)
            sb.AppendLine($"ReleaseNotesUrl: {config.ReleaseNotesUrl}");

        sb.AppendLine("ManifestType: defaultLocale");
        sb.AppendLine($"ManifestVersion: {config.ManifestVersion}");

        var path = Path.Combine(dir, $"{id}.locale.en-US.yaml");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteExtraLocaleManifest(
        string dir,
        string id,
        string version,
        WinGetLocale locale,
        string manifestVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PackageIdentifier: {id}");
        sb.AppendLine($"PackageVersion: {version}");
        sb.AppendLine($"PackageLocale: {locale.Locale}");
        sb.AppendLine($"Publisher: {locale.Publisher}");
        sb.AppendLine($"PackageName: {locale.PackageName}");

        if (locale.License is not null)
            sb.AppendLine($"License: {locale.License}");

        sb.AppendLine($"ShortDescription: {locale.ShortDescription}");

        if (locale.Description is not null)
            sb.AppendLine($"Description: {locale.Description}");

        sb.AppendLine("ManifestType: locale");
        sb.AppendLine($"ManifestVersion: {manifestVersion}");

        var path = Path.Combine(dir, $"{id}.locale.{locale.Locale}.yaml");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string MapArchitecture(ProcessorArchitecture arch) => arch switch
    {
        ProcessorArchitecture.X86 => "x86",
        ProcessorArchitecture.X64 => "x64",
        ProcessorArchitecture.Arm64 => "arm64",
        _ => "x64"
    };

    private static string MapInstallerType(WinGetInstallerType type) => type switch
    {
        WinGetInstallerType.Exe => "exe",
        _ => "msi"
    };

    private static string MapScope(InstallScope scope) => scope switch
    {
        InstallScope.PerMachine => "machine",
        InstallScope.PerUser => "user",
        _ => "machine"
    };
}
