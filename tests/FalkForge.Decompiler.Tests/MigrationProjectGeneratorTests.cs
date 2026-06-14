using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Tests for MigrationProjectGenerator — MSI path.
///
/// WHY these tests matter:
/// A consumer of "forge migrate" receives a generated C# project they can
/// immediately compile and run against their own FalkForge source tree.
/// The generated Program.cs must:
///   (a) call Installer.Build so the migrated installer actually produces an MSI at runtime,
///   (b) reference FalkForge.Compiler.Msi so MsiCompiler resolves at compile time,
///   (c) ProjectReference the caller's FalkForge source so no NuGet feed is required.
/// The csproj must target net10.0-windows (MSI compilation is Windows-only)
/// and include a payload/** glob so the user can drop payloads next to the project.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MigrationProjectGeneratorTests
{
    private const string DummySourcePath = "../../src";
    private const string ProjectName = "MyMigrated";

    private static MigrationOptions DefaultOptions() =>
        new(FalkForgeSourcePath: DummySourcePath, ProjectName: ProjectName);

    // ── helpers ──────────────────────────────────────────────────────────────

    private static MigrationResult RunWithMock(string? projectName = null)
    {
        // Use MockMsiTableAccess (standard pattern from MsiDecompilerTests) so the
        // test doesn't need a real .msi file and runs cross-machine without build artifacts.
        using var access = CreateStandardMockAccess();
        var generator = new MigrationProjectGenerator(new MsiDecompiler(access));
        var opts = new MigrationOptions(
            FalkForgeSourcePath: DummySourcePath,
            ProjectName: projectName ?? ProjectName);

        var result = generator.Generate("ignored.msi", opts);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    // ── result shape ─────────────────────────────────────────────────────────

    [Fact]
    public void Generate_MsiInput_ReturnsSuccess()
    {
        // A migration must succeed for valid MSI input — smoke test.
        using var access = CreateStandardMockAccess();
        var generator = new MigrationProjectGenerator(new MsiDecompiler(access));

        var result = generator.Generate("ignored.msi", DefaultOptions());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Generate_MsiInput_TextFilesHasThreeKeys()
    {
        // Migrator must produce exactly Program.cs, <ProjectName>.csproj, MIGRATION-REPORT.md —
        // any fewer and the output project is incomplete; any more is unexpected scope.
        var value = RunWithMock();

        Assert.Equal(3, value.TextFiles.Count);
        Assert.True(value.TextFiles.ContainsKey("Program.cs"));
        Assert.True(value.TextFiles.ContainsKey($"{ProjectName}.csproj"));
        Assert.True(value.TextFiles.ContainsKey("MIGRATION-REPORT.md"));
    }

    [Fact]
    public void Generate_MsiInput_UnmappedIsEmpty()
    {
        // MSI decompilation maps fully — no WiX-specific unmapped features.
        var value = RunWithMock();

        Assert.Empty(value.Unmapped);
    }

    // ── Program.cs ───────────────────────────────────────────────────────────

    [Fact]
    public void Generate_ProgramCs_ContainsMsiCompilerUsing()
    {
        // WHY: MsiCompiler lives in FalkForge.Compiler.Msi; the emitter doesn't add it,
        // so the generator must inject it. Without this using the generated file won't compile.
        var value = RunWithMock();
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("using FalkForge.Compiler.Msi;", prog);
    }

    [Fact]
    public void Generate_ProgramCs_ContainsInstallerBuildCall()
    {
        // WHY: Installer.Build(args, model, compiler) is the entry point that actually
        // compiles the MSI at runtime. Without it the migrated project does nothing.
        var value = RunWithMock();
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("return Installer.Build(args, model, new MsiCompiler());", prog);
    }

    [Fact]
    public void Generate_ProgramCs_ContainsBuilderBuildLine()
    {
        // Emitter ends with "var model = builder.Build();" — the generator must preserve it
        // so that the 'model' identifier is in scope for the Installer.Build call.
        var value = RunWithMock();
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("var model = builder.Build();", prog);
    }

    [Fact]
    public void Generate_ProgramCs_ContainsCoreUsings()
    {
        // Emitter emits using FalkForge / FalkForge.Builders / FalkForge.Models;
        // these must still be present after the generator wraps the fragment.
        var value = RunWithMock();
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("using FalkForge;", prog);
        Assert.Contains("using FalkForge.Builders;", prog);
        Assert.Contains("using FalkForge.Models;", prog);
    }

    [Fact]
    public void Generate_ProgramCs_InstallerBuildCallAfterBuilderBuild()
    {
        // WHY: 'model' must be declared before Installer.Build references it.
        // If the order is wrong the generated file won't compile.
        var value = RunWithMock();
        var prog = value.TextFiles["Program.cs"];

        var modelIdx = prog.IndexOf("var model = builder.Build();", StringComparison.Ordinal);
        var buildIdx = prog.IndexOf("return Installer.Build(", StringComparison.Ordinal);

        Assert.True(modelIdx >= 0, "builder.Build() line missing");
        Assert.True(buildIdx > modelIdx, "Installer.Build must come after builder.Build()");
    }

    // ── .csproj ──────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_Csproj_TargetsNet10Windows()
    {
        // MSI compilation requires Windows; targeting net10.0-windows ensures
        // Windows-only APIs (msi.dll P/Invoke) are available at compile time.
        var value = RunWithMock();
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains("net10.0-windows", csproj);
    }

    [Fact]
    public void Generate_Csproj_OutputTypeIsExe()
    {
        // Generated project must be a runnable executable, not a library.
        var value = RunWithMock();
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains("<OutputType>Exe</OutputType>", csproj);
    }

    [Fact]
    public void Generate_Csproj_EnablesImplicitUsings()
    {
        // The emitted Program.cs relies on System.* (Version, Guid) being globally
        // imported. Outside the repo the in-repo Directory.Build.props does not apply,
        // so the generated csproj must enable ImplicitUsings itself or the project will
        // not compile (CS0246 for Version/Guid).
        var value = RunWithMock();
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains("<ImplicitUsings>enable</ImplicitUsings>", csproj);
    }

    [Fact]
    public void Generate_Csproj_ReferencesFalkForgeCoreWithInjectedPath()
    {
        // WHY: Users build against their local FalkForge source, not a NuGet package.
        // The ProjectReference must use the path they passed in MigrationOptions.
        var value = RunWithMock();
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains($"{DummySourcePath}/FalkForge.Core/FalkForge.Core.csproj", csproj);
    }

    [Fact]
    public void Generate_Csproj_ReferencesCompilerMsiWithInjectedPath()
    {
        // Without Compiler.Msi reference MsiCompiler won't resolve.
        var value = RunWithMock();
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains($"{DummySourcePath}/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj", csproj);
    }

    [Fact]
    public void Generate_Csproj_ContainsPayloadGlob()
    {
        // payload/** glob lets users drop their payload files next to the project
        // and have them copied to output automatically.
        var value = RunWithMock();
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains("payload/**", csproj);
        Assert.Contains("PreserveNewest", csproj);
    }

    [Fact]
    public void Generate_Csproj_UsesMicrosoftNetSdk()
    {
        // Must be plain Microsoft.NET.Sdk (runnable exe), not FalkForge.Sdk.
        var value = RunWithMock();
        var csproj = value.TextFiles[$"{ProjectName}.csproj"];

        Assert.Contains("Microsoft.NET.Sdk", csproj);
        Assert.DoesNotContain("FalkForge.Sdk", csproj);
    }

    [Fact]
    public void Generate_Csproj_ProjectNameInFileName()
    {
        // Key in TextFiles must match options.ProjectName so caller knows what to write.
        const string name = "AcmeMigrated";
        var value = RunWithMock(projectName: name);

        Assert.True(value.TextFiles.ContainsKey($"{name}.csproj"));
    }

    // ── MIGRATION-REPORT.md ──────────────────────────────────────────────────

    [Fact]
    public void Generate_Report_ContainsSourceFileName()
    {
        // Report must identify which MSI was migrated for traceability.
        var value = RunWithMock();
        var report = value.TextFiles["MIGRATION-REPORT.md"];

        Assert.Contains("ignored.msi", report);
    }

    [Fact]
    public void Generate_Report_MentionsMsiType()
    {
        // Report must state the detected type so the reader knows which
        // decompiler path was used.
        var value = RunWithMock();
        var report = value.TextFiles["MIGRATION-REPORT.md"];

        Assert.Contains("MSI", report);
    }

    [Fact]
    public void Generate_Report_IsValidMarkdown()
    {
        // Must start with a Markdown heading — bare text is not a valid report.
        var value = RunWithMock();
        var report = value.TextFiles["MIGRATION-REPORT.md"];

        Assert.StartsWith("#", report.TrimStart());
    }

    // ── report honesty ───────────────────────────────────────────────────────

    [Fact]
    public void NotMigratedSection_WithEnvironmentVariables_NamesEnvironmentVariables()
    {
        // WHY: the report must HONESTLY disclose model features the emitter does not
        // yet emit. Environment variables present in the decompiled model are silently
        // lost, so the report must name them so the migrator knows to add them by hand.
        var model = new FalkForge.Models.PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            EnvironmentVariables =
            [
                new FalkForge.Models.EnvironmentVariableModel { Name = "PATH", Value = "x" }
            ]
        };

        var section = MigrationProjectGenerator.BuildNotMigratedSection(model);

        Assert.Contains("Not yet migrated", section);
        Assert.Contains("environment variable", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotMigratedSection_EmptyLongTail_SaysAllMapped()
    {
        // WHY: when no unemitted features are present the report must say so positively,
        // not leave the reader guessing whether something was dropped.
        var model = new FalkForge.Models.PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
        };

        var section = MigrationProjectGenerator.BuildNotMigratedSection(model);

        Assert.Contains("All present features were mapped.", section);
    }

    // ── error paths ──────────────────────────────────────────────────────────

    [Fact]
    public void Generate_NonExistentBundleInput_FailsGracefully()
    {
        // A .exe that is neither a readable FALKBUNDLE nor a WiX Burn bundle (here a
        // non-existent file) must fail gracefully via the bundle routing, not throw.
        var generator = new MigrationProjectGenerator();

        var result = generator.Generate("setup.exe", DefaultOptions());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Generate_UnknownExtension_ReturnsFailure()
    {
        // Unknown extension must not crash.
        var generator = new MigrationProjectGenerator();

        var result = generator.Generate("package.xyz", DefaultOptions());

        Assert.True(result.IsFailure);
    }

    // ── csproj XML well-formedness ─────────────────────────────────────────────

    [Fact]
    public void Generate_CsprojWithAmpersandInSourcePath_ProducesWellFormedXml()
    {
        // WHY (FIX 6): the source path is interpolated into a ProjectReference Include="..."
        // attribute. An unescaped '&' (or '<', '"') breaks the csproj so MSBuild cannot load
        // it. The value must be XML-escaped; parsing the result with XDocument must succeed.
        using var access = CreateStandardMockAccess();
        var generator = new MigrationProjectGenerator(new MsiDecompiler(access));
        var opts = new MigrationOptions(
            FalkForgeSourcePath: "../R&D/src & more",
            ProjectName: ProjectName);

        var result = generator.Generate("ignored.msi", opts);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        var csproj = result.Value.TextFiles[$"{ProjectName}.csproj"];

        // Must parse as well-formed XML (would throw on the raw '&').
        var doc = System.Xml.Linq.XDocument.Parse(csproj);
        Assert.NotNull(doc.Root);
    }

    // ── Program.cs single-injection invariant ──────────────────────────────────

    [Fact]
    public void Generate_ProgramCs_InjectsUsingAndEntryPointExactlyOnce()
    {
        // WHY (FIX 8): BuildProgramCs must inject the MsiCompiler using once and append the
        // entry point once. The previous dead first loop / buffer-scan rewrite risked
        // duplicate or misplaced injection; this pins the single-injection invariant.
        var value = RunWithMock();
        var prog = value.TextFiles["Program.cs"];

        Assert.Equal(1, CountSubstring(prog, "using FalkForge.Compiler.Msi;"));
        Assert.Equal(1, CountSubstring(prog, "return Installer.Build(args, model, new MsiCompiler());"));
    }

    private static int CountSubstring(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    // ── fixture ──────────────────────────────────────────────────────────────

    private static MockMsiTableAccess CreateStandardMockAccess()
    {
        return new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Test Application"],
                ["Manufacturer", "Contoso"],
                ["ProductVersion", "2.1.0"],
                ["UpgradeCode", "{12345678-1234-1234-1234-123456789012}"],
                ["ProductCode", "{87654321-4321-4321-4321-210987654321}"]
            ])
            .WithTable("Directory",
            [
                ["TARGETDIR", null, "SourceDir"],
                ["ProgramFilesFolder", "TARGETDIR", "."],
                ["INSTALLFOLDER", "ProgramFilesFolder", "TestApp"]
            ])
            .WithTable("Component",
            [
                ["comp1", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", "INSTALLFOLDER", "256", null, "file1"]
            ])
            .WithTable("File",
            [
                ["file1", "comp1", "APP~1|app.exe", "4096", "2.1.0", null, "0", "1"]
            ])
            .WithTable("Feature",
            [
                ["Complete", null, "Complete", "Full installation", "1", "1", "INSTALLFOLDER", "0"]
            ])
            .WithTable("FeatureComponents",
            [
                ["Complete", "comp1"]
            ])
            .WithTable("Registry",
            [
                ["reg1", "2", "SOFTWARE\\TestApp", "Version", "2.1.0", "comp1"]
            ]);
    }
}
