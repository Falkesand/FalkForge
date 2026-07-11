using System.Text.RegularExpressions;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// <c>forge init</c> is the zero-to-installer onboarding path: it must scaffold a project that
/// (a) references the single FalkForge meta-package at the tool's own single-source version —
/// never the 26 granular packages — and (b) contains a working fluent installer program, so that
/// <c>forge init &amp;&amp; dotnet run</c> yields a real MSI or bundle. These tests pin the
/// scaffold's structure and safety rails; the restore-build-run proof against a real feed lives
/// in FalkForge.Integration.Tests.OnboardingEndToEndTests.
/// </summary>
public sealed class InitCommandTests : IDisposable
{
    private readonly string _tempDir;

    public InitCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkInit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "init", null);

    private static (int ExitCode, TestConsoleOutput Console) Run(InitSettings settings)
    {
        var console = new TestConsoleOutput();
        var exitCode = new InitCommand(console).ExecuteSync(CreateContext(), settings);
        return (exitCode, console);
    }

    // ---- settings validation ----

    [Fact]
    public void Validate_UnknownType_ReturnsError()
    {
        var settings = new InitSettings { Type = "msix" };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("--type", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("msi")]
    [InlineData("bundle")]
    [InlineData("MSI")]
    public void Validate_KnownTypes_Succeed(string type)
    {
        var settings = new InitSettings { Type = type };

        Assert.True(settings.Validate().Successful);
    }

    // ---- msi scaffold ----

    [Fact]
    public void Execute_DefaultMsi_ScaffoldsBuildableProjectShape()
    {
        var (exitCode, _) = Run(new InitSettings { OutputDir = _tempDir, Name = "My App" });

        Assert.Equal(ExitCodes.Success, exitCode);

        // Project file name derives from the product name (file-system safe).
        var csproj = Path.Combine(_tempDir, "My_App.csproj");
        Assert.True(File.Exists(csproj), "scaffold must write the project file");
        Assert.True(File.Exists(Path.Combine(_tempDir, "Program.cs")), "scaffold must write Program.cs");
        Assert.True(File.Exists(Path.Combine(_tempDir, "payload", "readme.txt")),
            "scaffold must include a sample payload so the first build produces a non-empty installer");

        var csprojContent = File.ReadAllText(csproj);

        // ONE meta-package reference at the tool's own version — the whole onboarding story.
        var reference = Regex.Match(csprojContent,
            """<PackageReference Include="FalkForge" Version="([^"]+)"\s*/>""");
        Assert.True(reference.Success, $"csproj must reference the FalkForge meta-package: {csprojContent}");
        Assert.DoesNotContain("FalkForge.Core", csprojContent, StringComparison.Ordinal);

        // The referenced version is the single-source version the CLI itself carries
        // (build metadata stripped — NuGet versions never include +metadata).
        var expectedVersion = VersionInfo.CliVersion.Split('+')[0];
        Assert.Equal(expectedVersion, reference.Groups[1].Value);

        var program = File.ReadAllText(Path.Combine(_tempDir, "Program.cs"));
        Assert.Contains("Installer.Build(args", program, StringComparison.Ordinal);
        Assert.Contains("new MsiCompiler()", program, StringComparison.Ordinal);
        Assert.Contains("\"My App\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NoName_DerivesProductNameFromOutputDirectory()
    {
        var projectDir = Path.Combine(_tempDir, "AcmeSetup");
        Directory.CreateDirectory(projectDir);

        var (exitCode, _) = Run(new InitSettings { OutputDir = projectDir });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(projectDir, "AcmeSetup.csproj")));
        Assert.Contains("\"AcmeSetup\"",
            File.ReadAllText(Path.Combine(projectDir, "Program.cs")), StringComparison.Ordinal);
    }

    // ---- bundle scaffold ----

    [Fact]
    public void Execute_BundleType_ScaffoldsChainedBundleWithFreshGuids()
    {
        var (exitCode, _) = Run(new InitSettings { OutputDir = _tempDir, Type = "bundle", Name = "Suite" });

        Assert.Equal(ExitCodes.Success, exitCode);

        var program = File.ReadAllText(Path.Combine(_tempDir, "Program.cs"));
        Assert.Contains("Installer.BuildBundle(args", program, StringComparison.Ordinal);
        Assert.Contains("new BundleCompiler()", program, StringComparison.Ordinal);
        Assert.Contains("new MsiCompiler()", program, StringComparison.Ordinal);

        // BundleId + UpgradeCode must be freshly generated per scaffold — shared literals would
        // make two scaffolded products upgrade-collide on end-user machines.
        var guids = Regex.Matches(program, """new Guid\("([0-9a-fA-F-]{36})"\)""")
            .Select(m => Guid.Parse(m.Groups[1].Value))
            .ToList();
        Assert.True(guids.Count >= 2, "bundle scaffold must embed BundleId and UpgradeCode GUIDs");
        Assert.Equal(guids.Count, guids.Distinct().Count());

        var (secondExit, _) = Run(new InitSettings
        {
            OutputDir = Path.Combine(_tempDir, "second"),
            Type = "bundle",
            Name = "Suite"
        });
        Assert.Equal(ExitCodes.Success, secondExit);
        var secondGuids = Regex.Matches(
                File.ReadAllText(Path.Combine(_tempDir, "second", "Program.cs")),
                """new Guid\("([0-9a-fA-F-]{36})"\)""")
            .Select(m => Guid.Parse(m.Groups[1].Value));
        Assert.Empty(guids.Intersect(secondGuids));
    }

    // ---- clobber protection ----

    [Fact]
    public void Execute_ExistingFile_RefusesWithoutForce_AndWritesNothing()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Program.cs"), "// precious user code");

        var (exitCode, console) = Run(new InitSettings { OutputDir = _tempDir, Name = "App" });

        Assert.Equal(ExitCodes.ValidationFailure, exitCode);
        Assert.Contains(console.Errors, e => e.Contains("--force", StringComparison.Ordinal));
        Assert.Equal("// precious user code", File.ReadAllText(Path.Combine(_tempDir, "Program.cs")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "App.csproj")),
            "a refused init must not leave a partial scaffold behind");
    }

    [Fact]
    public void Execute_ExistingFile_ForceOverwrites()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Program.cs"), "// old");

        var (exitCode, _) = Run(new InitSettings { OutputDir = _tempDir, Name = "App", Force = true });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Installer.Build(args",
            File.ReadAllText(Path.Combine(_tempDir, "Program.cs")), StringComparison.Ordinal);
    }

    // ---- --from-publish ----

    [Fact]
    public void Execute_FromPublish_CopiesPublishOutputAsPayload()
    {
        var publishDir = Path.Combine(_tempDir, "publish");
        Directory.CreateDirectory(Path.Combine(publishDir, "sub"));
        File.WriteAllText(Path.Combine(publishDir, "app.exe.txt"), "app");
        File.WriteAllText(Path.Combine(publishDir, "sub", "data.txt"), "data");

        var projectDir = Path.Combine(_tempDir, "project");
        var (exitCode, _) = Run(new InitSettings
        {
            OutputDir = projectDir,
            Name = "App",
            FromPublish = publishDir
        });

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("app", File.ReadAllText(Path.Combine(projectDir, "payload", "app.exe.txt")));
        Assert.Equal("data", File.ReadAllText(Path.Combine(projectDir, "payload", "sub", "data.txt")));
        Assert.False(File.Exists(Path.Combine(projectDir, "payload", "readme.txt")),
            "the sample payload must not be emitted when real payload was supplied");
    }

    [Fact]
    public void Execute_FromPublishEqualsOutputDirectory_RefusesInsteadOfNestingPayloadIntoItself()
    {
        // Plausible first-run mistake: `forge init --from-publish .` executed inside the publish
        // folder with the default output directory. The payload target (outDir/payload) would sit
        // inside the copy source, so a live recursive copy re-picks freshly copied files and
        // runs away into payload/payload/... nesting. The command must refuse loudly instead.
        File.WriteAllText(Path.Combine(_tempDir, "app.exe.txt"), "app");

        var (exitCode, console) = Run(new InitSettings
        {
            OutputDir = _tempDir,
            Name = "App",
            FromPublish = _tempDir
        });

        Assert.Equal(ExitCodes.ValidationFailure, exitCode);
        Assert.Contains(console.Errors, e =>
            e.Contains("--from-publish", StringComparison.Ordinal) &&
            e.Contains("separate", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "payload")),
            "a refused init must not copy anything — and must never nest payload into itself");
        Assert.False(File.Exists(Path.Combine(_tempDir, "App.csproj")),
            "a refused init must not leave a partial scaffold behind");
    }

    [Fact]
    public void Execute_FromPublishAncestorOfOutputDirectory_Refuses()
    {
        // Same footgun one level up: the output directory nested anywhere inside the publish
        // source still places the payload target inside the enumerated tree.
        File.WriteAllText(Path.Combine(_tempDir, "app.exe.txt"), "app");
        var projectDir = Path.Combine(_tempDir, "installer");

        var (exitCode, console) = Run(new InitSettings
        {
            OutputDir = projectDir,
            Name = "App",
            FromPublish = _tempDir
        });

        Assert.Equal(ExitCodes.ValidationFailure, exitCode);
        Assert.Contains(console.Errors, e => e.Contains("--from-publish", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(projectDir, "payload")),
            "a refused init must not copy anything");
    }

    [Fact]
    public void Execute_FromPublishMissingDirectory_FailsLoud()
    {
        var (exitCode, console) = Run(new InitSettings
        {
            OutputDir = _tempDir,
            Name = "App",
            FromPublish = Path.Combine(_tempDir, "does-not-exist")
        });

        Assert.Equal(ExitCodes.RuntimeError, exitCode);
        Assert.Contains(console.Errors, e => e.Contains("does-not-exist", StringComparison.Ordinal));
    }
}
