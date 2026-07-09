using System.Text;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Emits the text artefacts (Program.cs, csproj, migration report) for the MSI
/// migration branch of <see cref="MigrationProjectGenerator"/>. Pure string builders —
/// no I/O, no state — split out so the generator stays a thin routing facade.
/// </summary>
internal static class MigrationMsiEmitter
{
    public static string BuildProgramCs(string emittedFragment)
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

    public static string BuildCsproj(MigrationOptions options)
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

    public static string BuildReport(string inputPath, MigrationOptions options, PackageModel model)
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
