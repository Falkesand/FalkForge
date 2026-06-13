using System.Runtime.Versioning;
using System.Text;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Generates a compilable FalkForge installer project from an existing installer binary.
///
/// Supported in this slice:
///   .msi / .msm — full MSI decompilation via <see cref="MsiDecompiler"/> (Windows-only).
///
/// Not yet supported (later slice):
///   .exe — bundle decompilation.
/// </summary>
public sealed class MigrationProjectGenerator
{
    private readonly MsiDecompiler? _msiDecompiler;

    /// <summary>
    /// Creates a generator that opens MSI files directly (real Windows MSI database).
    /// </summary>
    public MigrationProjectGenerator()
    {
        // _msiDecompiler is null; Windows-only path creates one on demand.
    }

    /// <summary>
    /// Creates a generator with an injected <see cref="MsiDecompiler"/> — primarily for testing
    /// so that a <see cref="MockMsiTableAccess"/> can be supplied without touching the filesystem.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public MigrationProjectGenerator(MsiDecompiler msiDecompiler)
    {
        _msiDecompiler = msiDecompiler;
    }

    /// <summary>
    /// Generates a migration project from <paramref name="inputPath"/>.
    /// Routes to the appropriate decompiler based on file extension.
    /// </summary>
    public Result<MigrationResult> Generate(string inputPath, MigrationOptions options)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        if (ext is ".msi" or ".msm")
        {
            if (!OperatingSystem.IsWindows())
                return Result<MigrationResult>.Failure(
                    ErrorKind.Validation,
                    "MSI migration requires Windows.");

            return GenerateFromMsi(inputPath, options);
        }

        return ext switch
        {
            ".exe" => Result<MigrationResult>.Failure(
                          ErrorKind.Validation,
                          "Bundle (.exe) migration is not yet implemented. This will be added in a later slice."),
            _      => Result<MigrationResult>.Failure(
                          ErrorKind.Validation,
                          $"Unrecognised installer extension '{ext}'. Supported: .msi, .msm.")
        };
    }

    // ── MSI branch ───────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private Result<MigrationResult> GenerateFromMsi(string inputPath, MigrationOptions options)
    {
        // Reuse injected decompiler (tests) or create a fresh one (production).
        var decompiler = _msiDecompiler ?? new MsiDecompiler();

        // Decompile to the model first so the report can honestly enumerate which
        // model features are still not emitted; emit C# from the same model.
        var modelResult = decompiler.Decompile(inputPath);
        if (modelResult.IsFailure)
            return Result<MigrationResult>.Failure(modelResult.Error);

        var model = modelResult.Value;
        var emittedFragment = new CSharpEmitter().Emit(model);

        var programCs   = BuildProgramCs(emittedFragment);
        var csproj      = BuildCsproj(options);
        var report      = BuildReport(inputPath, options, model);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.cs"]                            = programCs,
            [$"{options.ProjectName}.csproj"]         = csproj,
            ["MIGRATION-REPORT.md"]                   = report,
        };

        // Extract payload bytes only on the production path (a real MSI file on disk).
        // The injected-mock decompiler has no cabinet access, so its Payloads stay empty.
        IReadOnlyDictionary<string, byte[]> payloads =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (_msiDecompiler is null && File.Exists(inputPath))
        {
            var payloadResult = MsiPayloadExtractor.Extract(inputPath);
            if (payloadResult.IsFailure)
                return Result<MigrationResult>.Failure(payloadResult.Error);
            payloads = payloadResult.Value;
        }

        return Result<MigrationResult>.Success(
            new MigrationResult(files, [], payloads));
    }

    private static string BuildProgramCs(string emittedFragment)
    {
        // The emitter already emits:
        //   using FalkForge;
        //   using FalkForge.Builders;
        //   using FalkForge.Models;
        //   ...
        //   var model = builder.Build();
        //
        // We need to inject "using FalkForge.Compiler.Msi;" (for MsiCompiler)
        // and append the Installer.Build call that drives the actual MSI compilation.
        //
        // Strategy: inject the missing using right after the existing using block
        // (before the first blank line that separates usings from statements).

        const string msiUsing = "using FalkForge.Compiler.Msi;";
        const string entryPoint = "return Installer.Build(args, model, new MsiCompiler());";

        var sb = new StringBuilder(emittedFragment.Length + 128);

        if (!emittedFragment.Contains(msiUsing, StringComparison.Ordinal))
        {
            // Insert after the last "using ..." line in the block.
            // Find the index of the first blank line that ends the using block.
            var lines = emittedFragment.Split('\n');
            var insertedUsing = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                sb.AppendLine(line);
                if (!insertedUsing && string.IsNullOrWhiteSpace(line))
                {
                    // This blank line separates the using block from the body — insert before it.
                    // Back up: remove the blank line we just added, add using, re-add blank line.
                    // Actually: we already wrote it. Insert the using BEFORE the blank line.
                    // Redo: clear last blank line, write using, write blank line.
                    // Simpler approach: detect last using line, inject immediately after.
                    insertedUsing = true;
                }
            }

            // Simpler and more robust: rebuild with injection.
            sb.Clear();
            var usingBlockDone = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                if (!usingBlockDone && string.IsNullOrWhiteSpace(line) &&
                    sb.ToString().Contains("using ", StringComparison.Ordinal))
                {
                    // End of using block — inject our using before the blank separator.
                    sb.AppendLine(msiUsing);
                    usingBlockDone = true;
                }
                sb.AppendLine(line);
            }
        }
        else
        {
            sb.Append(emittedFragment);
        }

        // Append entry point (Installer.Build) after builder.Build().
        sb.AppendLine(entryPoint);

        return sb.ToString();
    }

    private static string BuildCsproj(MigrationOptions options)
    {
        // Forward slashes in XML paths — consistent cross-platform and readable.
        var src = options.FalkForgeSourcePath.Replace('\\', '/');

        return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{src}/FalkForge.Core/FalkForge.Core.csproj" />
                    <ProjectReference Include="{src}/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj" />
                  </ItemGroup>
                  <ItemGroup>
                    <None Include="payload/**" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """;
    }

    private static string BuildReport(string inputPath, MigrationOptions options, PackageModel model)
    {
        var fileName = Path.GetFileName(inputPath);
        var ext      = Path.GetExtension(inputPath).ToUpperInvariant().TrimStart('.');

        return $"""
                # Migration Report

                | Field | Value |
                |-------|-------|
                | Source file | `{fileName}` |
                | Detected type | {ext} |
                | Project name | {options.ProjectName} |
                | Mapping coverage | MSI decompilation maps the supported tables (see below). |

                ## Notes

                MSI decompilation covers: package metadata, features, files (payload entries are
                emitted as `files.Add("payload/...")` calls and the payload bytes are written to the
                `payload/` directory), registry entries, services, shortcuts, and properties.

                No unmapped WiX features (this is an MSI source, not a WiX bundle).

                {BuildNotMigratedSection(model)}
                """;
    }

    /// <summary>
    /// Honestly lists model feature categories that are present in the decompiled
    /// <paramref name="model"/> but are NOT yet emitted by <see cref="CSharpEmitter"/>,
    /// so the migrator knows exactly what to re-add by hand. Returns a positive
    /// "all mapped" note when nothing is dropped.
    /// </summary>
    internal static string BuildNotMigratedSection(PackageModel model)
    {
        var dropped = new List<string>();

        if (model.EnvironmentVariables.Count > 0) dropped.Add("environment variables");
        if (model.CustomActions.Count > 0)        dropped.Add("custom actions");
        if (model.CustomTables.Count > 0)          dropped.Add("custom tables");
        if (model.ExecuteSequenceActions.Count > 0 || model.UISequenceActions.Count > 0)
            dropped.Add("sequence scheduling");
        if (model.IniFiles.Count > 0)              dropped.Add("INI files");
        if (model.FileAssociations.Count > 0)      dropped.Add("file associations");
        if (model.Fonts.Count > 0)                 dropped.Add("fonts");
        if (model.Permissions.Count > 0)           dropped.Add("permissions");

        if (dropped.Count == 0)
            return "## Not yet migrated\n\nAll present features were mapped.";

        var sb = new StringBuilder("## Not yet migrated\n\n");
        sb.AppendLine(
            "The following features are present in the source installer but are NOT yet emitted "
            + "by the migrator. Re-add them manually in `Program.cs`:");
        sb.AppendLine();
        foreach (var item in dropped)
            sb.Append("- ").AppendLine(item);

        return sb.ToString().TrimEnd();
    }
}
