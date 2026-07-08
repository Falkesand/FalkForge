using System.Runtime.Versioning;
using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Verifies the Phase 2 logging instrumentation added to <see cref="MsiDecompiler"/>,
/// <see cref="BundleDecompiler"/>, and <see cref="WixBundleDecompiler"/>: the optional
/// <see cref="IFalkLogger"/> parameter is backward-compatible (defaults to null, no-op),
/// logs Info at decompile start/complete, Debug at each table read / emitter stage
/// (level-guarded), and Error (with a <c>code</c> structured property recovered from the
/// DEC*/BDC*/WBD* error prefix) before every failing return.
/// </summary>
public sealed class DecompilerLoggingTests
{
    private static MockMsiTableAccess CreateStandardMockAccess() =>
        new MockMsiTableAccess()
            .WithTable("Property",
            [
                ["ProductName", "Logged Application"],
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

    // ── MsiDecompiler ────────────────────────────────────────────────────────

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_Decompile_WithLogger_LogsInfoAtStartAndComplete()
    {
        using var access = CreateStandardMockAccess();
        var logger = new ListLogger();
        var decompiler = new MsiDecompiler(access, logger);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        var infoEntries = logger.EntriesAt(LogLevel.Info);
        Assert.Contains(infoEntries, e => e.Category == "MsiDecompiler" && e.Message.Contains("Decompiling MSI"));
        Assert.Contains(infoEntries, e => e.Category == "MsiDecompiler" && e.Message.Contains("Decompiled MSI") && e.Message.Contains("feature(s)"));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_Decompile_WithLogger_LogsDebugPerTableRead()
    {
        using var access = CreateStandardMockAccess();
        var logger = new ListLogger { MinimumLevel = LogLevel.Verbose };
        var decompiler = new MsiDecompiler(access, logger);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        var debugMessages = logger.EntriesAt(LogLevel.Debug).Select(e => e.Message).ToArray();
        Assert.Contains(debugMessages, m => m.Contains("'Property' table"));
        Assert.Contains(debugMessages, m => m.Contains("'Directory' table"));
        Assert.Contains(debugMessages, m => m.Contains("'Component' table"));
        Assert.Contains(debugMessages, m => m.Contains("'File' table"));
        Assert.Contains(debugMessages, m => m.Contains("'Feature' table"));
        Assert.Contains(debugMessages, m => m.Contains("'Registry' table"));

        // The reconstructor also logs a summary Debug entry.
        Assert.Contains(logger.Entries, e => e.Category == "MsiDecompiler" && e.Message.Contains("Reconstructed package model"));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_Decompile_WhenLoggerMinimumLevelAboveDebug_SkipsDebugEntries()
    {
        using var access = CreateStandardMockAccess();
        var logger = new ListLogger { MinimumLevel = LogLevel.Info };
        var decompiler = new MsiDecompiler(access, logger);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Debug);
        Assert.NotEmpty(logger.EntriesAt(LogLevel.Info));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_Decompile_WithoutLogger_StillSucceeds()
    {
        // Backward-compat guard: the logger parameter defaults to null across every ctor
        // overload, so every pre-existing "new MsiDecompiler(...)" call must keep working.
        using var access = CreateStandardMockAccess();
        var decompiler = new MsiDecompiler(access);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_TableQueryFailure_LogsErrorWithDec003Code()
    {
        using var access = new MockMsiTableAccess()
            .WithTableQueryFailure("Property", "Simulated read error");
        var logger = new ListLogger();
        var decompiler = new MsiDecompiler(access, logger);

        var result = decompiler.Decompile("ignored.msi");

        Assert.True(result.IsFailure);
        var errors = logger.EntriesAt(LogLevel.Error);
        var error = Assert.Single(errors, e => e.Category == "MsiDecompiler" && e.Message.Contains("Property"));
        Assert.NotNull(error.Properties);
        Assert.Equal("DEC003", error.Properties!["code"]);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_FileNotFound_LogsErrorWithDec001Code()
    {
        var logger = new ListLogger();
        var decompiler = new MsiDecompiler(logger);

        var result = decompiler.Decompile("nonexistent.msi");

        Assert.True(result.IsFailure);
        var errors = logger.EntriesAt(LogLevel.Error);
        var error = Assert.Single(errors);
        Assert.NotNull(error.Properties);
        Assert.Equal("DEC001", error.Properties!["code"]);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_DecompileToCSharp_WithLogger_LogsEmitterDebugAndInfo()
    {
        using var access = CreateStandardMockAccess();
        var logger = new ListLogger { MinimumLevel = LogLevel.Verbose };
        var decompiler = new MsiDecompiler(access, logger);

        var result = decompiler.DecompileToCSharp("ignored.msi");

        Assert.True(result.IsSuccess);
        Assert.Contains(logger.Entries, e => e.Category == "CSharpEmitter" && e.Message.Contains("Emitting C# source"));
        Assert.Contains(logger.Entries, e => e.Category == "CSharpEmitter" && e.Message.Contains("Emitted") && e.Message.Contains("character(s)"));
        Assert.Contains(logger.EntriesAt(LogLevel.Info), e => e.Category == "MsiDecompiler" && e.Message.Contains("Decompiled MSI to C# source"));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiDecompiler_DecompileToRecipe_WithLogger_LogsInfoAtStartAndComplete()
    {
        using var access = CreateStandardMockAccess();
        var logger = new ListLogger();
        var decompiler = new MsiDecompiler(access, logger);

        var result = decompiler.DecompileToRecipe("ignored.msi");

        Assert.True(result.IsSuccess);
        var infoEntries = logger.EntriesAt(LogLevel.Info);
        Assert.Contains(infoEntries, e => e.Category == "MsiDecompiler" && e.Message.Contains("Reading MSI recipe"));
        Assert.Contains(infoEntries, e => e.Category == "MsiDecompiler" && e.Message.Contains("Read MSI recipe"));
    }

    // ── BundleDecompiler ─────────────────────────────────────────────────────

    private static InstallerManifest CreateBundleManifest() => new()
    {
        Name = "Logged Bundle",
        Manufacturer = "Test Corp",
        Version = "1.0.0",
        BundleId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        UpgradeCode = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Scope = InstallScope.PerMachine,
        Packages = [],
        RelatedBundles = [],
        Chain = [],
    };

    [Fact]
    public void BundleDecompiler_Decompile_WithLogger_LogsInfoAndDebug()
    {
        var mock = new MockBundleAccess().WithManifest(CreateBundleManifest()).WithToc();
        var logger = new ListLogger { MinimumLevel = LogLevel.Verbose };
        var decompiler = new BundleDecompiler(mock, logger);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
        var infoEntries = logger.EntriesAt(LogLevel.Info);
        Assert.Contains(infoEntries, e => e.Category == "BundleDecompiler" && e.Message.Contains("Decompiling bundle"));
        Assert.Contains(infoEntries, e => e.Category == "BundleDecompiler" && e.Message.Contains("Decompiled bundle"));
        Assert.Contains(logger.EntriesAt(LogLevel.Debug), e => e.Message.Contains("Manifest read complete"));
        Assert.Contains(logger.EntriesAt(LogLevel.Debug), e => e.Message.Contains("TOC read complete"));
    }

    [Fact]
    public void BundleDecompiler_ManifestFailure_LogsErrorWithCode()
    {
        var mock = new MockBundleAccess()
            .WithManifestFailure(ErrorKind.BundleError, "BDC003: Bad manifest.")
            .WithToc();
        var logger = new ListLogger();
        var decompiler = new BundleDecompiler(mock, logger);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsFailure);
        var errors = logger.EntriesAt(LogLevel.Error);
        var error = Assert.Single(errors, e => e.Category == "BundleDecompiler");
        Assert.NotNull(error.Properties);
        Assert.Equal("BDC003", error.Properties!["code"]);
    }

    [Fact]
    public void BundleDecompiler_WithoutLogger_StillSucceeds()
    {
        var mock = new MockBundleAccess().WithManifest(CreateBundleManifest()).WithToc();
        var decompiler = new BundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void BundleDecompiler_DecompileToCSharp_WithLogger_LogsInfoWithCharacterCount()
    {
        var mock = new MockBundleAccess().WithManifest(CreateBundleManifest()).WithToc();
        var logger = new ListLogger();
        var decompiler = new BundleDecompiler(mock, logger);

        var result = decompiler.DecompileToCSharp("dummy.exe");

        Assert.True(result.IsSuccess);
        Assert.Contains(logger.EntriesAt(LogLevel.Info),
            e => e.Category == "BundleDecompiler" && e.Message.Contains("Decompiled bundle to C# source") && e.Message.Contains("character(s)"));
    }

    // ── WixBundleDecompiler ──────────────────────────────────────────────────

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_ManifestFailure_LogsErrorWithCode()
    {
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestFailure(ErrorKind.BundleError, "WBD003: PE file does not contain a .wixburn section.");
        var logger = new ListLogger();
        var decompiler = new WixBundleDecompiler(mock, logger);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsFailure);
        var errors = logger.EntriesAt(LogLevel.Error);
        var error = Assert.Single(errors, e => e.Category == "WixBundleDecompiler");
        Assert.NotNull(error.Properties);
        Assert.Equal("WBD003", error.Properties!["code"]);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WixBundleDecompiler_WithoutLogger_StillReturnsFailure()
    {
        var mock = new MockWixBurnAccess()
            .WithBundleId(Guid.NewGuid())
            .WithManifestFailure(ErrorKind.BundleError, "WBD003: PE file does not contain a .wixburn section.");
        var decompiler = new WixBundleDecompiler(mock);

        var result = decompiler.Decompile("dummy.exe");

        Assert.True(result.IsFailure);
        Assert.Contains("WBD003", result.Error.Message);
    }
}
