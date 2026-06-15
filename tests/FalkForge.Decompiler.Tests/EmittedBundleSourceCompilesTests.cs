using System.Reflection;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Builders;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Compilation guard for <see cref="BundleCSharpEmitter"/>.
///
/// WHY: the other bundle-emitter tests only assert on substrings — they never compile
/// the generated C#. That let a whole class of type-blind mismatches ship: the emitted
/// package body rendered <c>p.Version(...)</c>, <c>p.Property(...)</c>, <c>p.ExitCode(...)</c>,
/// <c>p.RemotePayload(...)</c> and <c>p.Container(...)</c> whenever the model field was set,
/// regardless of <see cref="BundlePackageType"/>. But the chain dispatches each package
/// type to a DIFFERENT builder with DIFFERENT members:
///   - Msi / Exe / NetRuntime → BundlePackageBuilder (has Version, Property, ExitCode,
///     RemotePayload, Container; NO KbArticle/PatchCode/TargetProductCode)
///   - MsuPackage             → MsuPackageBuilder (Id, DisplayName, Vital, KbArticle,
///     InstallCondition ONLY)
///   - MspPackage             → MspPackageBuilder (Id, DisplayName, Vital, PatchCode,
///     TargetProductCode, InstallCondition ONLY)
///   - BundlePackage          → NestedBundlePackageBuilder (Id, DisplayName, Vital,
///     InstallCondition ONLY)
/// The manifest mapper populates ALL model fields regardless of type, so a decompiled
/// MSU/MSP/BundlePackage can legitimately carry Version/Property/ExitCodes/RemotePayload/
/// ContainerId — the type-blind emitter then rendered <c>p.Version(...)</c> on a builder
/// that has no such member → CS1061 at compile time, only discovered when a migrated
/// project was actually built outside the repo.
///
/// This test builds a comprehensive <see cref="BundleModel"/> that exercises every
/// construct the emitter renders — including MSU/MSP/BundlePackage chain entries that
/// carry fields their dedicated builders LACK — wraps the emitted fragment exactly the
/// way <c>MigrationProjectGenerator.BuildBundleProgramCs</c> does, and compiles it
/// IN-MEMORY with Roslyn against the real FalkForge.Core and FalkForge.Compiler.Bundle
/// assemblies. Zero compile errors is the contract: a type-aware emitter must DROP the
/// fields the per-type builder cannot represent.
/// </summary>
public sealed class EmittedBundleSourceCompilesTests
{
    private static readonly Guid TestBundleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TestUpgradeCode = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Emit_ComprehensiveBundle_CompilesAgainstRealBuilderApi()
    {
        var model = BuildComprehensiveBundle();

        var fragment = BundleCSharpEmitter.Emit(model);
        var program = WrapAsMigrateGeneratorWould(fragment);

        var (success, diagnostics) = CompileInMemory(program);

        Assert.True(
            success,
            "Emitted bundle migration program failed to compile. Diagnostics:\n"
            + diagnostics
            + "\n\n--- Generated program ---\n"
            + program);
    }

    /// <summary>
    /// Faithful replica of <c>MigrationProjectGenerator.BuildBundleProgramCs</c>: inject the
    /// Compilation using right after the Builders using line, then append the runnable entry
    /// point after the emitted fragment (which already ends with <c>var bundle = b.Build();</c>).
    /// MUST stay in lockstep with the production wrapper; if that method changes, change this
    /// too (the compile guard is only meaningful if it wraps identically).
    /// </summary>
    private static string WrapAsMigrateGeneratorWould(string emittedFragment)
    {
        const string compilationUsing = "using FalkForge.Compiler.Bundle.Compilation;";
        const string buildersUsing = "using FalkForge.Compiler.Bundle.Builders;";
        const string entryPoint =
            "return Installer.BuildBundle(args, outputPath => new BundleCompiler().Compile(bundle, outputPath));";

        var lines = emittedFragment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new System.Text.StringBuilder(emittedFragment.Length + 256);

        var compilationUsingInjected = false;

        foreach (var line in lines)
        {
            if (!compilationUsingInjected &&
                line.Contains(buildersUsing, StringComparison.Ordinal))
            {
                sb.Append(line).Append('\n');
                sb.Append(compilationUsing).Append('\n');
                compilationUsingInjected = true;
                continue;
            }

            sb.Append(line).Append('\n');
        }

        sb.Append(entryPoint).Append('\n');

        return sb.ToString();
    }

    /// <summary>
    /// Compiles <paramref name="program"/> as a top-level-statement console application
    /// (providing the implicit <c>args</c> parameter and allowing the trailing <c>return</c>),
    /// referencing the real FalkForge assemblies plus the running framework. Returns the
    /// success flag and a formatted error dump.
    ///
    /// ImplicitUsings parity: the generated bundle csproj enables ImplicitUsings, so the
    /// emitted fragment relies on <c>System</c> (Guid) being globally imported. We add the
    /// equivalent global usings here so the compile reflects real generated-project conditions.
    /// </summary>
    private static (bool Success, string Diagnostics) CompileInMemory(string program)
    {
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
            assemblyName: "EmittedBundleMigrationProgram",
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
    /// References the real FalkForge.Core and FalkForge.Compiler.Bundle assemblies (so the
    /// emitted builder calls bind against the genuine API) plus every trusted-platform
    /// framework assembly of the running runtime.
    /// </summary>
    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        var references = new List<MetadataReference>();

        var trustedAssemblies =
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in trustedAssemblies)
            references.Add(MetadataReference.CreateFromFile(path));

        AddIfMissing(references, typeof(FalkForge.Installer).Assembly);   // FalkForge.Core
        AddIfMissing(references, typeof(BundleBuilder).Assembly);          // FalkForge.Compiler.Bundle

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
    /// A bundle that touches every construct <see cref="BundleCSharpEmitter"/> can render,
    /// AND deliberately attaches type-illegal fields to the MSU/MSP/BundlePackage chain entries
    /// (the reachable RED case the manifest mapper can produce). A type-aware emitter must drop
    /// those: MSU keeps only KbArticle/Id/DisplayName/Vital/InstallCondition; MSP keeps only
    /// PatchCode/TargetProductCode/Id/DisplayName/Vital/InstallCondition; BundlePackage keeps
    /// only Id/DisplayName/Vital/InstallCondition.
    /// </summary>
    private static BundleModel BuildComprehensiveBundle()
    {
        var remotePayload = new RemotePayloadModel
        {
            DownloadUrl = "https://example.com/payload.exe",
            Sha256Hash = "deadbeef",
            Size = 4096
        };

        return new BundleModel
        {
            Name = "Comprehensive Bundle",
            Manufacturer = "Acme Corp",
            Version = "1.2.3",
            BundleId = TestBundleId,
            UpgradeCode = TestUpgradeCode,
            Scope = InstallScope.PerUser,
            Packages = [],
            RelatedBundles =
            [
                new RelatedBundleModel
                {
                    BundleId = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
                    Relation = RelatedBundleRelation.Upgrade
                },
                new RelatedBundleModel
                {
                    BundleId = "{FFFFFFFF-1111-2222-3333-444444444444}",
                    Relation = RelatedBundleRelation.Addon
                }
            ],
            Containers =
            [
                new ContainerModel { Id = "MainContainer" }
            ],
            UiConfig = new BundleUiConfig
            {
                UiType = BundleUiType.BuiltIn,
                LicenseFile = "license.rtf"
            },
            Variables =
            [
                new BundleVariableModel(
                    Name: "INSTALLDIR",
                    Type: BundleVariableType.String,
                    DefaultValue: @"C:\Program Files\Acme",
                    Persisted: true,
                    Hidden: false,
                    Secret: false),
                new BundleVariableModel(
                    Name: "PORT",
                    Type: BundleVariableType.Numeric,
                    DefaultValue: "8080",
                    Persisted: false,
                    Hidden: true,
                    Secret: true),
                new BundleVariableModel(
                    Name: "MINVER",
                    Type: BundleVariableType.Version,
                    DefaultValue: "1.0.0",
                    Persisted: false,
                    Hidden: false,
                    Secret: false)
            ],
            Features =
            [
                new BundleFeatureModel
                {
                    Id = "Main",
                    Title = "Main Feature",
                    Description = "The main feature",
                    IsRequired = true,
                    PackageIds = ["app"]
                }
            ],
            Chain =
            [
                new RollbackBoundaryChainItem(new RollbackBoundaryModel { Id = "rbVital" }),
                new RollbackBoundaryChainItem(new RollbackBoundaryModel { Id = "rbOptional", Vital = false }),

                // MSI with a FULL body — every BundlePackageBuilder member is legal here.
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "app",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "My Application",
                    SourcePath = "app.msi",
                    Version = "2.5.0",
                    Vital = false,
                    InstallCondition = "VersionNT64",
                    ContainerId = "MainContainer",
                    RemotePayload = remotePayload,
                    ExitCodes = new Dictionary<int, ExitCodeBehavior>
                    {
                        [3010] = ExitCodeBehavior.RebootRequired
                    },
                    Properties = new Dictionary<string, string>
                    {
                        ["ADDLOCAL"] = "ALL"
                    }
                }),

                // EXE with remote payload + exit codes + properties — all legal.
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "setup",
                    Type = BundlePackageType.ExePackage,
                    DisplayName = "Setup Tool",
                    SourcePath = "setup.exe",
                    RemotePayload = remotePayload,
                    ExitCodes = new Dictionary<int, ExitCodeBehavior>
                    {
                        [1641] = ExitCodeBehavior.ScheduleReboot
                    },
                    Properties = new Dictionary<string, string>
                    {
                        ["SILENT"] = "1"
                    }
                }),

                // NetRuntime carrying Version — legal (also BundlePackageBuilder).
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "dotnet",
                    Type = BundlePackageType.NetRuntime,
                    DisplayName = ".NET Runtime",
                    SourcePath = "dotnet.exe",
                    Version = "10.0.0"
                }),

                // MSU carrying fields the MsuPackageBuilder LACKS — the reachable RED case.
                // Only KbArticle/Id/DisplayName/Vital/InstallCondition are legal; the emitter
                // must DROP Version/Property/ExitCodes/RemotePayload/ContainerId.
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "KB12345",
                    Type = BundlePackageType.MsuPackage,
                    DisplayName = "Security Update",
                    SourcePath = "update.msu",
                    KbArticle = "KB12345",
                    InstallCondition = "VersionNT >= 603",
                    Version = "1.0.0",
                    ContainerId = "MainContainer",
                    RemotePayload = remotePayload,
                    ExitCodes = new Dictionary<int, ExitCodeBehavior>
                    {
                        [3010] = ExitCodeBehavior.RebootRequired
                    },
                    Properties = new Dictionary<string, string>
                    {
                        ["IGNORED"] = "yes"
                    }
                }),

                // MSP carrying fields the MspPackageBuilder LACKS — the emitter must DROP
                // Version/Property; keep only PatchCode/TargetProductCode/Id/DisplayName/
                // Vital/InstallCondition.
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "patch1",
                    Type = BundlePackageType.MspPackage,
                    DisplayName = "Hotfix",
                    SourcePath = "hotfix.msp",
                    PatchCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
                    TargetProductCode = "{11111111-2222-3333-4444-555555555555}",
                    InstallCondition = "Installed",
                    Version = "1.0.1",
                    Properties = new Dictionary<string, string>
                    {
                        ["IGNORED"] = "yes"
                    }
                }),

                // BundlePackage carrying fields NestedBundlePackageBuilder LACKS — the emitter
                // must DROP Version/Property; keep only Id/DisplayName/Vital/InstallCondition.
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "nested",
                    Type = BundlePackageType.BundlePackage,
                    DisplayName = "Nested Installer",
                    SourcePath = "nested.exe",
                    InstallCondition = "VersionNT64",
                    Version = "3.0.0",
                    Properties = new Dictionary<string, string>
                    {
                        ["IGNORED"] = "yes"
                    }
                })
            ]
        };
    }
}
