using System.Text.Json;
using FalkForge;
using FalkForge.Cli.Models;
using FalkForge.Extensibility;
using FalkForge.Models;

namespace FalkForge.Cli;

public static partial class JsonConfigLoader
{
    public static Result<PackageModel> LoadFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return Result<PackageModel>.Failure(new Error(ErrorKind.FileNotFound, $"JSON file not found: {jsonPath}"));

        var json = File.ReadAllText(jsonPath);
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? Environment.CurrentDirectory;
        return LoadFromString(json, baseDirectory);
    }

    public static Result<PackageModel> LoadFromString(string json, string baseDirectory)
    {
        InstallerConfig config;
        try
        {
            config = JsonSerializer.Deserialize(json, InstallerConfigJsonContext.Default.InstallerConfig)
                ?? new InstallerConfig();
        }
        catch (JsonException ex)
        {
            return Result<PackageModel>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN001: Invalid JSON: {ex.Message}"));
        }

        return BuildPackageModel(config, baseDirectory);
    }

    /// <summary>
    /// Loads the optional <c>extensions</c> section of a forge JSON config and translates each
    /// present block (firewall / IIS / SQL / dotnet) into the corresponding real
    /// <see cref="IFalkForgeExtension"/> instance — the SAME types the C# fluent API attaches via
    /// <c>new MsiCompiler().Use(extension)</c>. <see cref="Commands.BuildCommand"/> attaches the
    /// returned extensions to the compiler so a JSON-authored firewall rule / IIS site / SQL script /
    /// .NET runtime search is emitted into the compiled MSI. Loaded separately from
    /// <see cref="LoadFromFile"/> (which returns only the <see cref="PackageModel"/>), mirroring
    /// <see cref="LoadSigningFromFile"/>. An absent extensions section, or one whose blocks are all
    /// empty, returns an empty list.
    /// </summary>
    public static Result<IReadOnlyList<IFalkForgeExtension>> LoadExtensionsFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(new Error(ErrorKind.FileNotFound, $"JSON file not found: {jsonPath}"));

        return LoadExtensionsFromString(File.ReadAllText(jsonPath));
    }

    /// <summary>String-input counterpart of <see cref="LoadExtensionsFromFile"/> (see there).</summary>
    public static Result<IReadOnlyList<IFalkForgeExtension>> LoadExtensionsFromString(string json)
    {
        InstallerConfig config;
        try
        {
            config = JsonSerializer.Deserialize(json, InstallerConfigJsonContext.Default.InstallerConfig)
                ?? new InstallerConfig();
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN001: Invalid JSON: {ex.Message}"));
        }

        if (config.Extensions is null || !HasAnyExtensionContent(config.Extensions))
            return Result<IReadOnlyList<IFalkForgeExtension>>.Success([]);

        // Field-level validation (JSN011–JSN014) fires FIRST so a malformed block reports the precise
        // missing field before any extension instance is constructed.
        var validation = ValidateExtensions(config.Extensions);
        if (validation.IsFailure)
            return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(validation.Error);

        return BuildExtensions(config.Extensions);
    }

    /// <summary>
    /// Loads and validates the optional <c>signing</c> section of a forge JSON config
    /// (structural validation only — no environment access; build-time resolution of
    /// env-referenced material happens in <c>SigningProviderFactory</c>).
    /// An absent signing section normalizes to a config with provider <c>none</c>.
    /// </summary>
    public static Result<SigningConfig> LoadSigningFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return Result<SigningConfig>.Failure(new Error(ErrorKind.FileNotFound, $"JSON file not found: {jsonPath}"));

        return LoadSigningFromString(File.ReadAllText(jsonPath));
    }

    /// <summary>String-input counterpart of <see cref="LoadSigningFromFile"/> (see there).</summary>
    public static Result<SigningConfig> LoadSigningFromString(string json)
    {
        InstallerConfig config;
        try
        {
            config = JsonSerializer.Deserialize(json, InstallerConfigJsonContext.Default.InstallerConfig)
                ?? new InstallerConfig();
        }
        catch (JsonException ex)
        {
            return Result<SigningConfig>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN001: Invalid JSON: {ex.Message}"));
        }

        return ValidateSigning(config.Signing);
    }
}
