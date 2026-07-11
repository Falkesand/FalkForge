namespace FalkForge.Cli;

/// <summary>
/// Produces the file set <c>forge init</c> scaffolds: a project referencing the single
/// <c>FalkForge</c> meta-package (the batteries-included onboarding story — never the 26
/// granular packages) and a minimal working fluent installer program mirroring the
/// hello-world demo shape. Pure content generation; the command owns all file IO.
/// </summary>
internal static class InitScaffolder
{
    internal const string SamplePayloadPath = "payload/readme.txt";

    /// <summary>
    /// Builds the relative-path → content map for a scaffold.
    /// </summary>
    /// <param name="productName">Human-readable product name (may contain spaces).</param>
    /// <param name="projectFileName">File-system-safe project file name without extension.</param>
    /// <param name="bundle"><c>true</c> for an EXE-bundle scaffold, <c>false</c> for MSI.</param>
    /// <param name="packageVersion">The FalkForge meta-package version to reference.</param>
    /// <param name="includeSamplePayload">
    /// Emit the sample payload file; <c>false</c> when real payload is prefilled from a
    /// published application folder.
    /// </param>
    internal static IReadOnlyDictionary<string, string> CreateFiles(
        string productName, string projectFileName, bool bundle, string packageVersion,
        bool includeSamplePayload)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [projectFileName + ".csproj"] = CreateProjectFile(packageVersion),
            ["Program.cs"] = bundle ? CreateBundleProgram(productName) : CreateMsiProgram(productName)
        };

        if (includeSamplePayload)
        {
            files[SamplePayloadPath] =
                $"""
                 {productName} sample payload.

                 Every file under payload/ is installed to the product's Program Files folder.
                 Replace this file with your application's files (or re-run
                 `forge init --from-publish <dir>` to prefill from a published build).
                 """ + Environment.NewLine;
        }

        return files;
    }

    private static string CreateProjectFile(string packageVersion) =>
        $"""
         <Project Sdk="Microsoft.NET.Sdk">
           <PropertyGroup>
             <OutputType>Exe</OutputType>
             <TargetFramework>net10.0-windows</TargetFramework>
             <Nullable>enable</Nullable>
             <ImplicitUsings>enable</ImplicitUsings>
           </PropertyGroup>
           <ItemGroup>
             <!-- The one FalkForge package: fluent API, MSI + bundle compilers, extensions,
                  and the bundle engine runtime arrive transitively. -->
             <PackageReference Include="FalkForge" Version="{packageVersion}" />
           </ItemGroup>
           <ItemGroup>
             <None Include="payload/**" CopyToOutputDirectory="PreserveNewest" />
           </ItemGroup>
         </Project>
         """ + Environment.NewLine;

    private static string CreateMsiProgram(string productName)
    {
        var product = EscapeStringLiteral(productName);
        return $$"""
                 using FalkForge;
                 using FalkForge.Compiler.Msi;
                 using FalkForge.Models;

                 // Build the installer:
                 //   dotnet run              -> writes the MSI to the current directory
                 //   dotnet run -- -o out    -> writes the MSI to out\
                 return Installer.Build(args, package =>
                 {
                     package.Name = "{{product}}";
                     package.Manufacturer = "My Company";
                     package.Version = new Version(1, 0, 0);

                     package.UseDialogSet(MsiDialogSet.Minimal);

                     package.Files(files => files
                         .FromDirectory("payload")
                         .To(KnownFolder.ProgramFiles / "My Company" / "{{product}}"));
                 }, new MsiCompiler());
                 """ + Environment.NewLine;
    }

    private static string CreateBundleProgram(string productName)
    {
        var product = EscapeStringLiteral(productName);
        // Fresh GUIDs per scaffold: shared BundleId/UpgradeCode literals would make two
        // scaffolded products upgrade-collide on end-user machines.
        var bundleId = Guid.NewGuid();
        var upgradeCode = Guid.NewGuid();
        return $$"""
                 using FalkForge;
                 using FalkForge.Builders;
                 using FalkForge.Compiler.Bundle.Builders;
                 using FalkForge.Compiler.Bundle.Compilation;
                 using FalkForge.Compiler.Msi;
                 using FalkForge.Models;

                 // Build the installer:
                 //   dotnet run              -> writes the MSI and the self-extracting EXE bundle
                 //                              to the current directory
                 //   dotnet run -- -o out    -> writes both to out\
                 //
                 // The bundle's PE front is the real NativeAOT FalkForge engine, resolved
                 // automatically from the FalkForge.Engine.Runtime.win-x64 package that the
                 // FalkForge meta-package references (it lands in this project's build output
                 // under engine\).
                 return Installer.BuildBundle(args, outputPath =>
                 {
                     // 1. Build the MSI the bundle chains.
                     var package = new PackageBuilder();
                     package.Name = "{{product}}";
                     package.Manufacturer = "My Company";
                     package.Version = new Version(1, 0, 0);

                     package.UseDialogSet(MsiDialogSet.Minimal);

                     package.Files(files => files
                         .FromDirectory("payload")
                         .To(KnownFolder.ProgramFiles / "My Company" / "{{product}}"));

                     var msi = new MsiCompiler().Compile(package.Build(), outputPath);
                     if (msi.IsFailure)
                         return msi;

                     // 2. Chain it into a self-extracting EXE bundle.
                     var bundle = new BundleBuilder()
                         .Name("{{product}}")
                         .Manufacturer("My Company")
                         .Version("1.0.0")
                         .BundleId(new Guid("{{bundleId}}"))
                         .UpgradeCode(new Guid("{{upgradeCode}}"))
                         .Scope(InstallScope.PerMachine)
                         .UseBuiltInUI(themeColor: "#0078D4")
                         .Chain(chain => chain
                             .MsiPackage(msi.Value, p => p
                                 .Id("MainMsi")
                                 .DisplayName("{{product}}")
                                 .Vital(true)))
                         .Build();

                     return new BundleCompiler().Compile(bundle, outputPath);
                 });
                 """ + Environment.NewLine;
    }

    private static string EscapeStringLiteral(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);
}
