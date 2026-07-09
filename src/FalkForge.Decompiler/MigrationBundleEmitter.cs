using System.Text;

namespace FalkForge.Decompiler;

/// <summary>
/// Emits the text artefacts (Program.cs, csproj, migration report) for the bundle
/// migration branch of <see cref="MigrationProjectGenerator"/> — covering both native
/// FALKBUNDLE and WiX Burn sources. Pure string builders — no I/O, no state — split out
/// so the generator stays a thin routing facade.
/// </summary>
internal static class MigrationBundleEmitter
{
    /// <summary>
    /// Converts the emitter's real-builder fragment into the runnable entry point by injecting
    /// the <c>using FalkForge.Compiler.Bundle.Compilation;</c> namespace right after the
    /// Builders using line and appending the compile call after the fragment's final
    /// <c>var bundle = b.Build();</c> line:
    /// <code>
    /// var b = new BundleBuilder();
    /// ... b.X(...) ...
    /// var bundle = b.Build();
    /// return Installer.BuildBundle(args, outputPath =&gt; new BundleCompiler().Compile(bundle, outputPath));
    /// </code>
    /// The emitter already emits the fragment in this shape, so no text-transform is needed —
    /// only the Compilation using injection and the entry-point append.
    /// </summary>
    internal static string BuildBundleProgramCs(string emittedFragment)
    {
        const string compilationUsing = "using FalkForge.Compiler.Bundle.Compilation;";
        const string buildersUsing = "using FalkForge.Compiler.Bundle.Builders;";
        const string entryPoint =
            "return Installer.BuildBundle(args, outputPath => new BundleCompiler().Compile(bundle, outputPath));";

        var lines = emittedFragment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new StringBuilder(emittedFragment.Length + 256);

        var compilationUsingInjected = false;

        foreach (var line in lines)
        {
            // Inject the Compilation using right after the Builders using line.
            if (!compilationUsingInjected &&
                line.Contains(buildersUsing, StringComparison.Ordinal))
            {
                sb.AppendLine(line);
                sb.AppendLine(compilationUsing);
                compilationUsingInjected = true;
                continue;
            }

            sb.AppendLine(line);
        }

        sb.AppendLine(entryPoint);

        return sb.ToString();
    }

    internal static string BuildBundleCsproj(MigrationOptions options)
    {
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
                    <ProjectReference Include="{src}/FalkForge.Compiler.Bundle/FalkForge.Compiler.Bundle.csproj" />
                  </ItemGroup>
                  <ItemGroup>
                    <None Include="payload/**" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """;
    }

    internal static string BuildBundleReport(
        string inputPath,
        MigrationOptions options,
        string detectedType,
        IReadOnlyList<WixUnmappedFeature> unmapped)
    {
        var fileName = Path.GetFileName(inputPath);

        var sb = new StringBuilder();
        sb.AppendLine("# Migration Report");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|-------|-------|");
        sb.AppendLine($"| Source file | `{fileName}` |");
        sb.AppendLine($"| Detected type | {detectedType} |");
        sb.AppendLine($"| Project name | {options.ProjectName} |");
        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine(
            "Bundle decompilation maps the chain (packages and rollback boundaries), bundle "
            + "identity, variables, features, related bundles, containers, and UI configuration. "
            + "Native FalkForge bundles also have their embedded chain payloads extracted into the "
            + "`payload/` directory; each chained package references its payload-relative path.");
        sb.AppendLine();
        sb.AppendLine("## Not yet migrated");
        sb.AppendLine();
        sb.AppendLine(
            "UI assets (logo, theme, watermark, banner), container download URLs, and custom UI "
            + "project paths are not preserved. Re-add them manually in `Program.cs` if required.");

        if (unmapped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Unmapped WiX Burn features");
            sb.AppendLine();
            sb.AppendLine(
                "The following WiX Burn features have no FalkForge equivalent and were NOT migrated. "
                + "Re-implement them manually:");
            sb.AppendLine();
            foreach (var feature in unmapped)
            {
                sb.Append("- **").Append(feature.Category).Append("**: ").AppendLine(feature.Description);
                if (!string.IsNullOrEmpty(feature.OriginalXml) && feature.OriginalXml.Length <= 200)
                    sb.Append("  - `").Append(feature.OriginalXml).AppendLine("`");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
