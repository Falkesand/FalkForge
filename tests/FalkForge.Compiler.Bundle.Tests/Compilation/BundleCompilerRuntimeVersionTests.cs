using FalkForge.Diagnostics;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// The compiler cannot tell whether a header-only MZ file will verify the envelope it signs. It must
/// still build (the whole suite embeds such files) and it must say that it did not check, once per
/// binary, so a real engine or companion that lost its version resource does not pass in silence.
/// </summary>
public sealed class BundleCompilerRuntimeVersionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _payloadPath;
    private readonly string _fakeBinary;

    public BundleCompilerRuntimeVersionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleRtVer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _payloadPath = Path.Combine(_tempDir, "payload.msi");
        File.WriteAllBytes(_payloadPath, [0xD0, 0xCF, 0x11, 0xE0, 0x00]);
        _fakeBinary = Path.Combine(_tempDir, "fake.exe");
        File.WriteAllBytes(_fakeBinary, [(byte)'M', (byte)'Z', 0xE1, 0xE7]);
    }

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    private BundleModel BuildModel() => new()
    {
        Name = "RtVerBundle",
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
            }
        }.AsReadOnly(),
        Containers = [],
    };

    private static bool HasCode(LogEntry e, string code)
        => e.Category == "BundleCompiler"
        && e.Level == LogLevel.Warning
        && e.Properties is not null
        && e.Properties.TryGetValue("code", out var actual)
        && actual == code;

    [Fact]
    public void Compile_ExplicitEngineStubWithoutVersionResource_BuildsAndLogsBDL038ForTheEngine()
    {
        var logger = new ListLogger();
        var compiler = new BundleCompiler
        {
            EngineStubPath = _fakeBinary,
            ElevationCompanionPath = _fakeBinary,
            Logger = logger,
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-engine"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(logger.Entries, e => HasCode(e, "BDL038") && e.Message.Contains("engine at", StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_CompanionWithoutVersionResource_BuildsAndLogsBDL038ForTheCompanion()
    {
        var logger = new ListLogger();
        var compiler = new BundleCompiler
        {
            EngineStubPath = _fakeBinary,
            ElevationCompanionPath = _fakeBinary,
            Logger = logger,
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-companion"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(logger.Entries, e => HasCode(e, "BDL038") && e.Message.Contains("elevation companion at", StringComparison.Ordinal));
        Assert.Equal(2, logger.Entries.Count(e => HasCode(e, "BDL038")));
    }

    [Fact]
    public void Compile_PlaceholderStub_LogsNoBDL038()
    {
        // The placeholder is an empty file by design, not a runtime that could be stale.
        var logger = new ListLogger();
        var compiler = new BundleCompiler { AllowPlaceholderStub = true, Logger = logger };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-placeholder"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.DoesNotContain(logger.Entries, e => HasCode(e, "BDL038"));
    }
}
