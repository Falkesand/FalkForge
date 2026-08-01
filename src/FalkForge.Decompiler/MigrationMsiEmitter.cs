using System.Collections.Frozen;
using System.Text;
using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Emits the text artefacts (Program.cs, csproj, migration report) for the MSI
/// migration branch of <see cref="MigrationProjectGenerator"/>. Pure string builders —
/// no I/O, no state — split out so the generator stays a thin routing facade.
/// </summary>
internal static class MigrationMsiEmitter
{
    internal static string BuildProgramCs(string emittedFragment)
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
            // Inject the using before the first blank line that follows the using block.
            // Track whether a using line has been seen with a flag (no per-line buffer scan).
            var lines = emittedFragment.Split('\n');
            var sawUsing = false;
            var usingBlockDone = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                if (!usingBlockDone && sawUsing && string.IsNullOrWhiteSpace(line))
                {
                    // End of using block — inject our using before the blank separator.
                    sb.AppendLine(msiUsing);
                    usingBlockDone = true;
                }

                if (line.StartsWith("using ", StringComparison.Ordinal))
                    sawUsing = true;

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

    internal static string BuildCsproj(MigrationOptions options)
    {
        // Forward slashes in XML paths — consistent cross-platform and readable.
        // XML-escape the operator-supplied source path before it lands in an XML attribute;
        // a '&', '<', or '"' would otherwise produce a malformed csproj that will not load.
        var src = System.Security.SecurityElement.Escape(
            options.FalkForgeSourcePath.Replace('\\', '/'));

        return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
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

    internal static string BuildReport(
        string inputPath, MigrationOptions options, PackageModel model, IReadOnlyList<string> unmappedTableNames)
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

                {BuildNotMigratedSection(model, unmappedTableNames)}
                """;
    }

    /// <summary>
    /// MSI tables the migrator demonstrably reads: every core <see cref="TableReadSchema{TRow}.TableName"/>
    /// this assembly's <see cref="MsiDecompiler"/> queries, built from the schema objects themselves so
    /// this set cannot silently drift from the code that actually does the reading. Extension-contributed
    /// tables (<see cref="Recipe.MsiReadRecipe.ExtensionRows"/>) are unioned in per-call, since which
    /// extensions are registered varies by caller.
    /// </summary>
    private static readonly FrozenSet<string> CoreMappedTableNames = new[]
    {
        PropertySchema.Schema.TableName,
        DirectorySchema.Schema.TableName,
        ComponentSchema.Schema.TableName,
        FileSchema.Schema.TableName,
        FeatureSchema.Schema.TableName,
        FeatureComponentsSchema.Schema.TableName,
        RegistrySchema.Schema.TableName,
        ServiceSchema.Schema.TableName,
        ShortcutSchema.Schema.TableName,
        UpgradeSchema.Schema.TableName,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Computes the MSI tables present in the source database that neither a core schema
    /// nor a registered extension contributor reads. Deliberately NOT a hardcoded deny-list
    /// of "known extension tables" — that would still silently drop a table nobody thought
    /// of. Instead this is "present minus demonstrably read", so an unknown table is loud by
    /// construction. MSI-internal catalog tables (leading underscore, e.g. <c>_Tables</c>,
    /// <c>_Columns</c>, <c>_Validation</c>) are excluded — those were never migratable data.
    /// Result is sorted for deterministic report output.
    /// </summary>
    internal static IReadOnlyList<string> ComputeUnmappedTableNames(
        IReadOnlyList<string> allTableNames,
        IReadOnlyDictionary<string, IReadOnlyList<object>> extensionRows)
    {
        var mapped = extensionRows.Count == 0
            ? CoreMappedTableNames
            : CoreMappedTableNames.Union(extensionRows.Keys).ToFrozenSet(StringComparer.Ordinal);

        return allTableNames
            .Where(name => !name.StartsWith('_') && !mapped.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Honestly lists model feature categories that are present in the decompiled
    /// <paramref name="model"/> but are NOT yet emitted by <see cref="CSharpEmitter"/>, and
    /// MSI tables present in the source (<paramref name="unmappedTableNames"/>) that no core
    /// schema or extension contributor reads at all — so the migrator knows exactly what was
    /// dropped and cannot be recovered from the generated project. Returns a positive
    /// "all mapped" note only when BOTH lists are empty.
    /// </summary>
    internal static string BuildNotMigratedSection(PackageModel model, IReadOnlyList<string> unmappedTableNames)
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

        if (dropped.Count == 0 && unmappedTableNames.Count == 0)
            return "## Not yet migrated\n\nAll present features were mapped.";

        var sb = new StringBuilder("## Not yet migrated\n\n");

        if (dropped.Count > 0)
        {
            sb.AppendLine(
                "The following features are present in the source installer but are NOT yet emitted "
                + "by the migrator. Re-add them manually in `Program.cs`:");
            sb.AppendLine();
            foreach (var item in dropped)
                sb.Append("- ").AppendLine(item);
        }

        if (unmappedTableNames.Count > 0)
        {
            if (dropped.Count > 0)
                sb.AppendLine();

            sb.AppendLine(
                "The following MSI tables are present in the source database but are not read by the "
                + "migrator at all. Their rows were dropped before ever reaching the decompiled model "
                + "and cannot be recovered from the generated project:");
            sb.AppendLine();
            foreach (var table in unmappedTableNames)
                sb.Append("- ").AppendLine(table);
        }

        return sb.ToString().TrimEnd();
    }
}
