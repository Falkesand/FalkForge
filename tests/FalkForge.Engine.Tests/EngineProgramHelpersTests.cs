namespace FalkForge.Engine.Tests;

using FalkForge.Engine;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// Characterization spec for <see cref="EngineProgramHelpers"/> — pins the exact exit-code mapping
/// and SBOM-extraction behavior these helpers had as private methods on <c>Program</c> before this
/// extraction.
/// </summary>
public sealed class EngineProgramHelpersTests : IDisposable
{
    private readonly string _tempDir;

    public EngineProgramHelpersTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge_Tests_EngineProgramHelpers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData(EngineTerminalState.Completed, 0)]
    [InlineData(EngineTerminalState.Cancelled, 2)]
    [InlineData(EngineTerminalState.RolledBack, 3)]
    [InlineData(EngineTerminalState.Failed, 1)]
    public void ToExitCode_MapsEachTerminalStateToItsProcessExitCode(EngineTerminalState state, int expected)
    {
        Assert.Equal(expected, EngineProgramHelpers.ToExitCode(state));
    }

    [Fact]
    public void ToExitCode_UnknownState_FallsBackToOne()
    {
        // Intent: the switch expression's default arm returns 1 for any value outside the four
        // known terminal states (e.g. a future enum member added without updating this mapping) —
        // fail as a generic error rather than throwing.
        Assert.Equal(1, EngineProgramHelpers.ToExitCode((EngineTerminalState)999));
    }

    private static InstallerManifest MakeManifest(string? sbomAttestation) => new()
    {
        Name = "TestProduct",
        Manufacturer = "TestCorp",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerUser,
        SbomAttestation = sbomAttestation
    };

    [Fact]
    public void ExtractSbom_NoAttestation_ReturnsOne_AndWritesNoFile()
    {
        var outputPath = Path.Combine(_tempDir, "sbom.json");

        var exitCode = EngineProgramHelpers.ExtractSbom(MakeManifest(sbomAttestation: null), outputPath);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void ExtractSbom_AttestationPresent_ReturnsZero_AndWritesItVerbatim()
    {
        var outputPath = Path.Combine(_tempDir, "sbom.json");
        const string attestation = "{\"bomFormat\":\"CycloneDX\"}";

        var exitCode = EngineProgramHelpers.ExtractSbom(MakeManifest(attestation), outputPath);

        Assert.Equal(0, exitCode);
        Assert.Equal(attestation, File.ReadAllText(outputPath));
    }
}
