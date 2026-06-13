using System.Reflection;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Compilation guard for <see cref="CSharpEmitter"/>.
///
/// WHY: the other emitter tests only assert on substrings — they never compile the
/// generated C#. That let a whole class of property-vs-method / nonexistent-member
/// mismatches ship: the emitted code looked plausible but did not build against the
/// real FalkForge builder API (CS1955 "cannot be used like a method", CS0117/CS1061
/// missing members). Those bugs only surfaced when a migrated project was actually
/// compiled outside the repo.
///
/// This test builds a comprehensive <see cref="PackageModel"/> that exercises every
/// construct the emitter knows how to render, wraps the emitted fragment exactly the
/// way the migrate generator does (inject the MSI compiler using + append the
/// Installer.Build entry point), and compiles it IN-MEMORY with Roslyn against the
/// real FalkForge.Core and FalkForge.Compiler.Msi assemblies. Zero compile errors is
/// the contract: if the emitter renders a call that does not exist, this fails loud
/// with the full diagnostic list.
/// </summary>
public sealed class EmittedSourceCompilesTests
{
    [Fact]
    public void Emit_ComprehensiveModel_CompilesAgainstRealBuilderApi()
    {
        var model = BuildComprehensiveModel();

        var fragment = new CSharpEmitter().Emit(model);
        var program = WrapAsMigrateGeneratorWould(fragment);

        var (success, diagnostics) = CompileInMemory(program);

        Assert.True(
            success,
            "Emitted migration program failed to compile. Diagnostics:\n"
            + diagnostics
            + "\n\n--- Generated program ---\n"
            + program);
    }

    /// <summary>
    /// Mirrors <c>MigrationProjectGenerator.BuildProgramCs</c>: inject the MSI compiler
    /// using after the emitter's using block, then append the runnable entry point that
    /// drives the real MSI compilation. Kept in lockstep with the production wrapper so
    /// this test reflects the actual generated Program.cs.
    /// </summary>
    private static string WrapAsMigrateGeneratorWould(string fragment)
    {
        const string msiUsing = "using FalkForge.Compiler.Msi;";
        const string entryPoint = "return Installer.Build(args, model, new MsiCompiler());";

        var lines = fragment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new System.Text.StringBuilder(fragment.Length + 128);
        var usingBlockDone = false;
        var sawUsing = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("using ", StringComparison.Ordinal))
                sawUsing = true;

            if (!usingBlockDone && sawUsing && string.IsNullOrWhiteSpace(line))
            {
                sb.Append(msiUsing).Append('\n');
                usingBlockDone = true;
            }

            sb.Append(line).Append('\n');
        }

        sb.Append(entryPoint).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Compiles <paramref name="program"/> as a top-level-statement console application
    /// (which gives the generated code its implicit <c>args</c> parameter and allows the
    /// trailing <c>return</c>), referencing the real FalkForge assemblies plus the running
    /// framework. Returns the success flag and a formatted error+warning dump.
    ///
    /// ImplicitUsings parity: the generated csproj enables ImplicitUsings, so the emitted
    /// fragment relies on <c>System</c> (Version, Guid) being globally imported. We add the
    /// equivalent global usings here so the compile reflects real generated-project conditions.
    /// </summary>
    private static (bool Success, string Diagnostics) CompileInMemory(string program)
    {
        // Match the SDK ImplicitUsings set the generated csproj will enable, so System.*
        // (Version, Guid) and the collection types resolve without explicit usings — the
        // emitter deliberately omits them, relying on ImplicitUsings.
        const string globalUsings =
            "global using global::System;\n"
            + "global using global::System.Collections.Generic;\n"
            + "global using global::System.IO;\n"
            + "global using global::System.Linq;\n"
            + "global using global::System.Threading;\n"
            + "global using global::System.Threading.Tasks;\n";

        var programTree = CSharpSyntaxTree.ParseText(program);
        var usingsTree = CSharpSyntaxTree.ParseText(globalUsings);

        var references = BuildMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "EmittedMigrationProgram",
            syntaxTrees: [usingsTree, programTree],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                nullableContextOptions: NullableContextOptions.Enable));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (result.Success)
            return (true, string.Empty);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());

        return (false, string.Join('\n', errors));
    }

    /// <summary>
    /// References the real FalkForge.Core and FalkForge.Compiler.Msi assemblies (so the
    /// emitted builder calls bind against the genuine API) plus every trusted-platform
    /// framework assembly of the running runtime (System.Runtime, netstandard, etc.).
    /// </summary>
    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        var references = new List<MetadataReference>();

        var trustedAssemblies =
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in trustedAssemblies)
            references.Add(MetadataReference.CreateFromFile(path));

        // Ensure the FalkForge assemblies are present even if not on the TPA list.
        AddIfMissing(references, typeof(PackageModel).Assembly);        // FalkForge.Core
        AddIfMissing(references, typeof(MsiCompiler).Assembly);          // FalkForge.Compiler.Msi

        return references;
    }

    private static void AddIfMissing(List<MetadataReference> references, Assembly assembly)
    {
        if (references.OfType<PortableExecutableReference>()
            .All(r => !string.Equals(r.FilePath, assembly.Location, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
    }

    /// <summary>
    /// A package that touches every construct <see cref="CSharpEmitter"/> can render:
    /// metadata (name/manufacturer/version/upgrade code/scope/architecture/description),
    /// a feature with a child + description + required flag, a registry entry, a service
    /// with description/start mode/account, a shortcut with location/description/arguments,
    /// a property, a file rooted at a known folder, a major upgrade, and a downgrade.
    /// </summary>
    private static PackageModel BuildComprehensiveModel()
    {
        return new PackageModel
        {
            Name = "Comprehensive App",
            Manufacturer = "Acme Corp",
            Version = new Version(2, 3, 4),
            UpgradeCode = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            Scope = InstallScope.PerUser,
            Architecture = ProcessorArchitecture.X86,
            Description = "A comprehensive test package",
            Features =
            [
                new FeatureModel
                {
                    Id = "Main",
                    Title = "Main Feature",
                    Description = "The main feature",
                    IsRequired = true,
                    Children =
                    [
                        new FeatureModel
                        {
                            Id = "Extra",
                            Title = "Extra Feature",
                            Description = "An optional add-on"
                        }
                    ]
                }
            ],
            Files =
            [
                new FileEntryModel
                {
                    SourcePath = "app.exe",
                    TargetDirectory = KnownFolder.ProgramFiles / "Acme" / "App",
                    FileName = "app.exe"
                }
            ],
            RegistryEntries =
            [
                new RegistryEntryModel
                {
                    Root = RegistryRoot.LocalMachine,
                    Key = "SOFTWARE\\Acme\\App",
                    ValueName = "Version",
                    Value = "2.3.4"
                }
            ],
            Services =
            [
                new ServiceModel
                {
                    Name = "AcmeSvc",
                    DisplayName = "Acme Service",
                    Executable = "svc.exe",
                    Description = "Background worker",
                    StartMode = ServiceStartMode.Manual,
                    Account = ServiceAccount.LocalService
                }
            ],
            Shortcuts =
            [
                new ShortcutModel
                {
                    Name = "Acme App",
                    TargetFile = "app.exe",
                    Locations = [ShortcutLocation.Desktop],
                    Description = "Launch Acme App",
                    Arguments = "--start"
                }
            ],
            Properties =
            [
                new PropertyModel { Name = "ACME_MODE", Value = "release" }
            ],
            MajorUpgrade = new MajorUpgradeModel { AllowSameVersionUpgrades = true },
            Downgrade = new DowngradeModel { AllowDowngrades = false, ErrorMessage = "No downgrades" }
        };
    }
}
