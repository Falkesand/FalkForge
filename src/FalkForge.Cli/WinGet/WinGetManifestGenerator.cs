using System.Text;
using System.Text.RegularExpressions;

namespace FalkForge.Cli.WinGet;

public static partial class WinGetManifestGenerator
{
    [GeneratedRegex(@"[^A-Za-z0-9.\-_]")]
    private static partial Regex InvalidIdentifierChars();

    public static string SanitizePackageIdentifier(string raw)
        => InvalidIdentifierChars().Replace(raw, string.Empty);

    private static string EscapeYaml(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static Result<string> Generate(WinGetManifestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sb = new StringBuilder();
        sb.AppendLine("# yaml-language-server: $schema=https://aka.ms/winget-manifest.singleton.1.6.0.schema.json");
        sb.AppendLine($"PackageIdentifier: \"{EscapeYaml(options.PackageIdentifier)}\"");
        sb.AppendLine($"PackageVersion: \"{EscapeYaml(options.PackageVersion)}\"");
        sb.AppendLine($"PackageLocale: \"{EscapeYaml(options.PackageLocale)}\"");
        sb.AppendLine($"Publisher: \"{EscapeYaml(options.Publisher)}\"");
        sb.AppendLine($"PackageName: \"{EscapeYaml(options.PackageName)}\"");
        sb.AppendLine($"License: \"{EscapeYaml(options.License)}\"");
        sb.AppendLine($"ShortDescription: \"{EscapeYaml(options.ShortDescription)}\"");
        sb.AppendLine("Installers:");
        sb.AppendLine($"- Architecture: \"{EscapeYaml(options.Architecture)}\"");
        sb.AppendLine($"  InstallerType: \"{EscapeYaml(options.InstallerType)}\"");
        sb.AppendLine($"  InstallerUrl: \"{EscapeYaml(options.InstallerUrl)}\"");
        sb.AppendLine($"  InstallerSha256: \"{EscapeYaml(options.InstallerSha256)}\"");
        sb.AppendLine("ManifestType: singleton");
        sb.AppendLine("ManifestVersion: 1.6.0");
        return Result<string>.Success(sb.ToString());
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
