using System.Runtime.Versioning;
using FalkForge.Diagnostics;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Verifies the Phase 1 logging instrumentation added to <see cref="MsiAuthoring"/>,
/// <see cref="MsiCompiler"/>, and <see cref="CabinetBuilder"/>: the optional
/// <see cref="IFalkLogger"/> parameter is backward-compatible (defaults to null, no-op),
/// logs the documented pipeline steps, and — the highest-value fix — surfaces the two
/// previously-silently-swallowed non-fatal failures (SBOM sidecar write, ICE
/// infrastructure failure) as <see cref="LogLevel.Warning"/> entries instead of dropping
/// them entirely.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LoggingInstrumentationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"LogTest_{Guid.NewGuid():N}");

    public LoggingInstrumentationTests()
    {
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", null);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
        }
    }

    private (string sourceFile, string outputDir) CreatePackageInputs(string label)
    {
        var sourceDir = Path.Combine(_tempDir, $"{label}_source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, $"fake executable content for {label}");

        var outputDir = Path.Combine(_tempDir, $"{label}_output");
        Directory.CreateDirectory(outputDir);

        return (sourceFile, outputDir);
    }

    [Fact]
    public void Compile_WithLogger_LogsInfoAtStartAndComplete()
    {
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithLogger_LogsInfoAtStartAndComplete));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "LoggedApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "LoggedApp"));
        });

        var logger = new ListLogger();
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var infoEntries = logger.EntriesAt(LogLevel.Info);
        Assert.Contains(infoEntries, e => e.Category == "MsiAuthoring" && e.Message.Contains("Compiling package 'LoggedApp'"));
        Assert.Contains(infoEntries, e => e.Category == "MsiAuthoring" && e.Message.Contains("Compile complete") && e.Message.Contains("bytes"));
    }

    [Fact]
    public void Compile_WithLogger_LogsDebugStepBoundaries()
    {
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithLogger_LogsDebugStepBoundaries));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "StepLoggedApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "StepLoggedApp"));
        });

        var logger = new ListLogger { MinimumLevel = LogLevel.Verbose };
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var debugMessages = logger.EntriesAt(LogLevel.Debug).Select(e => e.Message).ToArray();
        Assert.Contains(debugMessages, m => m.Contains("Step 2:"));
        Assert.Contains(debugMessages, m => m.Contains("Step 4:"));
        Assert.Contains(debugMessages, m => m.Contains("Step 5:"));
        Assert.Contains(debugMessages, m => m.Contains("Step 6:"));

        // Producer-level debug entries come from MsiRecipeBuilder, one per built-in table producer.
        Assert.Contains(logger.Entries, e => e.Category == "MsiRecipeBuilder" && e.Message.Contains("Producer"));

        // Per-cabinet debug entry comes from CabinetBuilder itself.
        Assert.Contains(logger.Entries, e => e.Category == "CabinetBuilder" && e.Message.Contains("Building cabinet"));
    }

    [Fact]
    public void Compile_WhenLoggerMinimumLevelAboveDebug_SkipsDebugEntries()
    {
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WhenLoggerMinimumLevelAboveDebug_SkipsDebugEntries));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "QuietApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "QuietApp"));
        });

        var logger = new ListLogger { MinimumLevel = LogLevel.Info };
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Debug);
        Assert.NotEmpty(logger.EntriesAt(LogLevel.Info));
    }

    [Fact]
    public void Compile_WithoutLogger_StillSucceeds()
    {
        // Backward-compat guard: the logger parameter defaults to null across every ctor
        // overload, so every pre-existing "new MsiCompiler().Compile(...)" call must keep
        // working unchanged.
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithoutLogger_StillSucceeds));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "NoLoggerApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "NoLoggerApp"));
        });

        var compiler = new MsiCompiler();
        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
    }

    [Fact]
    public void Compile_WhenSbomSidecarWriteFails_LogsWarningAndStillSucceeds()
    {
        // Regression test for the highest-value fix in the logging design: the SBOM sidecar
        // write used to be discarded unconditionally (`_ = sbomResult;`). Force the write to
        // fail by pre-creating a directory at the exact path SbomHelper will try to open as a
        // file — FileStream(..., FileMode.Create, ...) throws, SbomHelper catches it and
        // returns Result<Unit>.Failure(ErrorKind.IoError, ...). Compile must still succeed
        // (SBOM remains non-fatal) but must now log a Warning instead of staying silent.
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WhenSbomSidecarWriteFails_LogsWarningAndStillSucceeds));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "SbomFailApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SbomFailApp"));
            p.Sbom();
        });

        var msiFileName = $"{FileNameSanitizer.Sanitize(package.Name)}-{package.Version.ToString(3)}.msi";
        var sidecarPath = Path.Combine(outputDir, msiFileName + ".cdx.json");
        Directory.CreateDirectory(sidecarPath); // occupies the path the SBOM writer needs as a file

        var logger = new ListLogger();
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile should still succeed when SBOM sidecar fails, got: {(result.IsFailure ? result.Error.Message : "")}");

        var warnings = logger.EntriesAt(LogLevel.Warning);
        var sbomWarning = Assert.Single(warnings, e => e.Category == "MsiAuthoring" && e.Message.Contains("SBOM"));
        Assert.NotNull(sbomWarning.Properties);
        Assert.Equal("IoError", sbomWarning.Properties!["code"]);
    }

    [Fact]
    public void Compile_WhenIceValidationCannotRun_LogsWarningAndStillSucceeds()
    {
        // Regression test for the second silently-swallowed failure: when IceValidator.Validate
        // returns Result.Failure (e.g. a configured CUB path that does not exist), the old code
        // only branched on IsSuccess and silently dropped the failure. It must now log a Warning
        // and still let the overall compile succeed (ICE is opt-in / non-fatal).
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WhenIceValidationCannotRun_LogsWarningAndStillSucceeds));
        var bogusCub = Path.Combine(_tempDir, "does-not-exist.cub");
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IceFailApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IceFailApp"));
            p.Ice(i => i.CubFilePath(bogusCub));
        });

        var logger = new ListLogger();
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile should still succeed when ICE infra fails, got: {(result.IsFailure ? result.Error.Message : "")}");

        var warnings = logger.EntriesAt(LogLevel.Warning);
        var iceWarning = Assert.Single(warnings, e => e.Category == "MsiAuthoring" && e.Message.Contains("ICE"));
        Assert.NotNull(iceWarning.Properties);
        Assert.Equal("FileNotFound", iceWarning.Properties!["code"]);
    }

    [Fact]
    public void Compile_WhenDialogCustomizationValidationFails_LogsErrorWithRuleCode()
    {
        // DLG001 fires deterministically when InsertStep references a step name that no
        // extension has registered. Mirrors MsiAuthoringDialogStepRegistrationTests'
        // Compile_unregistered_insert_step_still_produces_DLG001, but also asserts the new
        // Error log entry (category + "code" property) added by this phase.
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WhenDialogCustomizationValidationFails_LogsErrorWithRuleCode));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "DlgFailApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "DlgFailApp"));
            p.UseDialogSet(FalkForge.Models.MsiDialogSet.Minimal, cfg =>
                cfg.InsertStep("UnknownStep", FalkForge.Models.StockDialog.Welcome));
        });

        var logger = new ListLogger();
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsFailure, "Expected DLG001 validation failure but compilation succeeded.");
        Assert.Contains("DLG001", result.Error.Message);

        var errors = logger.EntriesAt(LogLevel.Error);
        var dlgError = Assert.Single(errors, e => e.Category == "MsiAuthoring" && e.Message.Contains("Dialog customization"));
        Assert.NotNull(dlgError.Properties);
        Assert.Contains("DLG001", dlgError.Properties!["code"]);
    }

    [Fact]
    public void Compile_WithComponentContributorExtensionReturningNoFiles_LogsNoEXT002Warning()
    {
        // GetAdditionalFiles output is now routed through the same file-emission path as regular
        // package files (see ComponentContributorEmissionTests for the positive emission case), so
        // EXT002 is retired entirely — a registered contributor that happens to return zero files
        // still logs no warning, since nothing was dropped.
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithComponentContributorExtensionReturningNoFiles_LogsNoEXT002Warning));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "ComponentContribApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "ComponentContribApp"));
        });

        var logger = new ListLogger();
        var compiler = new MsiCompiler(new WindowsFileSystem(), [new ComponentContributingExtension()], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.DoesNotContain(logger.Entries, e => HasCode(e, "EXT002"));
    }

    [Fact]
    public void Compile_WithoutComponentContributor_LogsNoEXT002Warning()
    {
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithoutComponentContributor_LogsNoEXT002Warning));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "NoContribApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "NoContribApp"));
        });

        var logger = new ListLogger();
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.DoesNotContain(logger.Entries, e => HasCode(e, "EXT002"));
    }

    [Fact]
    public void Compile_WithRegistryDryRunContributor_LogsEXT003Warning()
    {
        // An extension that registers an IDryRunContributor through the registry gets it discarded by
        // the compile pipeline; EXT003 makes that non-silent instead of dropping it without a trace.
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithRegistryDryRunContributor_LogsEXT003Warning));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "DryRunContribApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "DryRunContribApp"));
        });

        var logger = new ListLogger();
        var compiler = new MsiCompiler(new WindowsFileSystem(), [new DryRunContributingExtension()], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var ext003 = Assert.Single(logger.EntriesAt(LogLevel.Warning),
            e => e.Category == "MsiAuthoring" && HasCode(e, "EXT003"));
        Assert.Contains("dry-run", ext003.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_WithoutDryRunContributor_LogsNoEXT003Warning()
    {
        var (sourceFile, outputDir) = CreatePackageInputs(nameof(Compile_WithoutDryRunContributor_LogsNoEXT003Warning));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "NoDryRunApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "NoDryRunApp"));
        });

        var logger = new ListLogger();
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);

        var result = compiler.Compile(package, outputDir);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.DoesNotContain(logger.Entries, e => HasCode(e, "EXT003"));
    }

    private static bool HasCode(LogEntry entry, string code)
        => entry.Properties is not null
        && entry.Properties.TryGetValue("code", out var actual)
        && actual == code;

    private sealed class ComponentContributingExtension : IFalkForgeExtension, IComponentContributor
    {
        public string Name => "TestComponentContributor";
        public void Register(IExtensionRegistry registry) => registry.RegisterComponentContributor(this);
        public IReadOnlyList<FileEntryModel> GetAdditionalFiles(ExtensionContext context) => [];
    }

    private sealed class DryRunContributingExtension : IFalkForgeExtension, IDryRunContributor
    {
        public string Name => "TestDryRunContributor";
        public void Register(IExtensionRegistry registry) => registry.RegisterDryRunContributor(this);
        public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) => [];
    }
}
