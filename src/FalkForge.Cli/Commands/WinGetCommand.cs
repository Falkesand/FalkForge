using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using FalkForge.Cli.Settings;
using FalkForge.Models;
using FalkForge.WinGet;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Generates WinGet manifest files from an existing MSI package.
/// Windows-only: requires msi.dll P/Invoke to read MSI metadata.
/// </summary>
public sealed class WinGetCommand : Command<WinGetSettings>
{
    private readonly IConsoleOutput _console;

    public WinGetCommand() : this(new SpectreConsoleOutput()) { }

    public WinGetCommand(IConsoleOutput console)
    {
        _console = console;
    }

    public override int Execute([NotNull] CommandContext context, [NotNull] WinGetSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("The winget command requires Windows (msi.dll).");
            return ExitCodes.RuntimeError;
        }

        var msiPath = Path.GetFullPath(settings.MsiPath);

        if (!File.Exists(msiPath))
        {
            _console.WriteError($"File not found: {msiPath}");
            return ExitCodes.RuntimeError;
        }

        // Read MSI metadata
        var inspectResult = MsiInspector.Inspect(msiPath);
        if (inspectResult.IsFailure)
        {
            _console.WriteError(inspectResult.Error.Message);
            return ExitCodes.FromErrorKind(inspectResult.Error.Kind);
        }

        var inspection = inspectResult.Value;

        // Compute SHA256 of the MSI file
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(msiPath)));

        // Build a minimal PackageModel from inspected metadata
        var package = new PackageModel
        {
            Name = inspection.ProductName ?? Path.GetFileNameWithoutExtension(msiPath),
            Manufacturer = inspection.Manufacturer ?? "Unknown",
            Version = Version.TryParse(inspection.Version, out var v) ? v : new Version(1, 0, 0),
            ProductCode = Guid.TryParse(inspection.ProductCode?.Trim('{', '}'), out var pc) ? pc : Guid.Empty
        };

        var config = new WinGetConfig
        {
            PackageIdentifier = settings.PackageIdentifier!,
            InstallerUrl = settings.InstallerUrl,
            License = settings.License!,
            ShortDescription = settings.ShortDescription!
        };

        var outputDir = Path.GetFullPath(settings.OutputDir);
        var result = WinGetManifestWriter.Write(package, config, outputDir, sha256, Path.GetFileName(msiPath));

        if (result.IsFailure)
        {
            _console.WriteError(result.Error.Message);
            return ExitCodes.FromErrorKind(result.Error.Kind);
        }

        _console.MarkupLine($"[green]WinGet manifests written to:[/] {Markup.Escape(result.Value)}");
        return ExitCodes.Success;
    }
}
