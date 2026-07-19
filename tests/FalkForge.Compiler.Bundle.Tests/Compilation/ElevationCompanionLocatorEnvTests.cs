using FalkForge.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Migration-equivalence pin: <see cref="ElevationCompanionLocator.Resolve"/>'s public 5-arg
/// overload must still read the REAL FALKFORGE_ELEVATION_COMPANION process environment variable
/// end to end (the other coverage for this locator, <c>ElevationCompanionEmbeddingTests</c>,
/// exercises full bundle compilation and never sets this specific env var directly).
/// </summary>
public sealed class ElevationCompanionLocatorEnvTests : IDisposable
{
    private readonly string _tempDir;

    public ElevationCompanionLocatorEnvTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ElevCompanionEnv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteFakeCompanion()
    {
        var path = Path.Combine(_tempDir, "FalkForge.Engine.Elevation.exe");
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Resolve_ReadsRealEnvironmentVariable_WithoutConsultingEngineResolver()
    {
        var companion = WriteFakeCompanion();
        Environment.SetEnvironmentVariable(ElevationCompanionLocator.EnvironmentVariableName, companion);
        try
        {
            var result = ElevationCompanionLocator.Resolve(
                explicitCompanionPath: null,
                explicitStubPath: null,
                allowPlaceholderStub: false,
                omitCompanion: false,
                // The env var is authoritative when set, so a real bundle compile never falls
                // through to engine resolution — proven by making the fallback throw if invoked.
                engineResolver: () => throw new InvalidOperationException(
                    "engineResolver must not be invoked when the env var resolves the companion."));

            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            Assert.Equal(companion, result.Value.ResolvedPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ElevationCompanionLocator.EnvironmentVariableName, null);
        }
    }
}
