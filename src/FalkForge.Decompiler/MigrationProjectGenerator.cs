using System.Runtime.Versioning;
using System.Text;

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

        var csharpResult = decompiler.DecompileToCSharp(inputPath);
        if (csharpResult.IsFailure)
            return Result<MigrationResult>.Failure(csharpResult.Error);

        var emittedFragment = csharpResult.Value;

        var programCs   = BuildProgramCs(emittedFragment);
        var csproj      = BuildCsproj(options);
        var report      = BuildReport(inputPath, options);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.cs"]                            = programCs,
            [$"{options.ProjectName}.csproj"]         = csproj,
            ["MIGRATION-REPORT.md"]                   = report,
        };

        return Result<MigrationResult>.Success(
            new MigrationResult(files, []));
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

    private static string BuildReport(string inputPath, MigrationOptions options)
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
                | Mapping coverage | Full — MSI decompilation maps all supported tables. |

                ## Notes

                MSI decompilation covers: package metadata, features, registry entries, services,
                shortcuts, and properties. File payload entries are reconstructed in the model but
                the generated `Program.cs` does not yet emit `files.Add(...)` calls — place payload
                files in the `payload/` directory and add them manually to `Program.cs`.

                No unmapped WiX features (this is an MSI source, not a WiX bundle).
                """;
    }
}
