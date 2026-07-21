using FalkForge.Diagnostics;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Verifies the bundle compiler's authoring-honesty warnings: inputs it accepts but does not yet
/// fully materialize must surface a loud, discoverable warning instead of being silently ignored.
/// Covers external container download URLs (BDL035), including the negative case (no warning when the
/// URL is absent) and backward compatibility (no <see cref="BundleCompiler.Logger"/> configured ⇒ no
/// crash, warning suppressed).
/// <para>
/// Also pins the RETIREMENT of BDL034: per-package MSI feature selection is now honored end-to-end
/// (the engine advertises the MSI's features at detect time and applies the interactive choice as
/// <c>ADDLOCAL</c> at plan time), so <c>EnableFeatureSelection</c> must NOT emit a "not yet honored"
/// warning any more.
/// </para>
/// </summary>
public sealed class BundleCompilerAuthoringWarningTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _payloadPath;

    public BundleCompilerAuthoringWarningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleAuthWarn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _payloadPath = Path.Combine(_tempDir, "payload.msi");
        File.WriteAllBytes(_payloadPath, [0xD0, 0xCF, 0x11, 0xE0, 0x00]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private BundleModel BuildModel(
        bool enableFeatureSelection = false,
        string? containerDownloadUrl = null,
        string name = "AuthWarnBundle") => new()
    {
        Name = name,
        Manufacturer = "Contoso",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Packages = new List<BundlePackageModel>
        {
            new()
            {
                Id = "payload.msi",
                SourcePath = _payloadPath,
                Type = BundlePackageType.MsiPackage,
                DisplayName = "Payload",
                EnableFeatureSelection = enableFeatureSelection,
            }
        }.AsReadOnly(),
        Containers = containerDownloadUrl is null
            ? []
            : new List<ContainerModel> { new() { Id = "extern", DownloadUrl = containerDownloadUrl } }.AsReadOnly(),
    };

    private BundleCompiler NewCompiler(IFalkLogger? logger)
        => new() { AllowPlaceholderStub = true, Logger = logger };

    private static bool HasCode(LogEntry e, string code)
        => e.Category == "BundleCompiler"
        && e.Level == LogLevel.Warning
        && e.Properties is not null
        && e.Properties.TryGetValue("code", out var actual)
        && actual == code;

    // ── BDL034 retired: per-package feature selection is now honored ─────────

    [Fact]
    public void Compile_WithEnableFeatureSelection_DoesNotLogBDL034Warning()
    {
        // BDL034 was an authoring-honesty warning for the era when EnableFeatureSelection was accepted
        // but never turned into an ADDLOCAL selection. The runtime loop is now wired (advertise → picker
        // → ADDLOCAL), so setting the flag is a materialized feature and must emit no "not honored" warning.
        var logger = new ListLogger();
        var result = NewCompiler(logger).Compile(
            BuildModel(enableFeatureSelection: true), Path.Combine(_tempDir, "out-bdl034"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.DoesNotContain(logger.Entries, e => HasCode(e, "BDL034"));
    }

    // ── BDL035: external container download URL ──────────────────────────────

    [Fact]
    public void Compile_WithContainerDownloadUrl_LogsBDL035Warning()
    {
        var logger = new ListLogger();
        var result = NewCompiler(logger).Compile(
            BuildModel(containerDownloadUrl: "https://cdn.example.com/extern.cab"),
            Path.Combine(_tempDir, "out-bdl035"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var warning = Assert.Single(logger.Entries, e => HasCode(e, "BDL035"));
        Assert.Contains("extern", warning.Message, StringComparison.Ordinal);
        Assert.Contains("embedded", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_WithoutContainerDownloadUrl_LogsNoBDL035Warning()
    {
        var logger = new ListLogger();
        var result = NewCompiler(logger).Compile(
            BuildModel(containerDownloadUrl: null), Path.Combine(_tempDir, "out-no-bdl035"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.DoesNotContain(logger.Entries, e => HasCode(e, "BDL035"));
    }

    // ── Backward compatibility: no logger configured ─────────────────────────

    [Fact]
    public void Compile_WithoutLogger_StillSucceedsWhenAuthoringWarningsWouldFire()
    {
        var model = BuildModel(
            enableFeatureSelection: true,
            containerDownloadUrl: "https://cdn.example.com/extern.cab");

        var result = NewCompiler(logger: null).Compile(model, Path.Combine(_tempDir, "out-nolog"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }
}
