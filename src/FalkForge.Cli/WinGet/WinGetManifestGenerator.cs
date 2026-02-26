using System.Text;
using System.Text.RegularExpressions;

namespace FalkForge.Cli.WinGet;

public static partial class WinGetManifestGenerator
{
    [GeneratedRegex(@"[^A-Za-z0-9.\-]")]
    private static partial Regex InvalidIdentifierChars();

    public static string SanitizePackageIdentifier(string raw)
        => InvalidIdentifierChars().Replace(raw, string.Empty);

    public static Result<string> Generate(WinGetManifestOptions options)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# yaml-language-server: $schema=https://aka.ms/winget-manifest.singleton.1.6.0.schema.json");
            sb.AppendLine($"PackageIdentifier: {options.PackageIdentifier}");
            sb.AppendLine($"PackageVersion: {options.PackageVersion}");
            sb.AppendLine($"PackageLocale: {options.PackageLocale}");
            sb.AppendLine($"Publisher: {options.Publisher}");
            sb.AppendLine($"PackageName: {options.PackageName}");
            sb.AppendLine($"License: {options.License}");
            sb.AppendLine($"ShortDescription: {options.ShortDescription}");
            sb.AppendLine("Installers:");
            sb.AppendLine($"- Architecture: {options.Architecture}");
            sb.AppendLine($"  InstallerType: {options.InstallerType}");
            sb.AppendLine($"  InstallerUrl: {options.InstallerUrl}");
            sb.AppendLine($"  InstallerSha256: {options.InstallerSha256}");
            sb.AppendLine("ManifestType: singleton");
            sb.AppendLine("ManifestVersion: 1.6.0");
            return Result<string>.Success(sb.ToString());
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(ErrorKind.IoError, $"WinGet manifest generation failed: {ex.Message}");
        }
    }

    public static Result<Unit> GenerateToFile(WinGetManifestOptions options, string filePath)
    {
        var result = Generate(options);
        if (result.IsFailure)
            return Result<Unit>.Failure(result.Error);

        try
        {
            File.WriteAllText(filePath, result.Value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"Failed to write WinGet manifest to {filePath}: {ex.Message}");
        }
    }
}
