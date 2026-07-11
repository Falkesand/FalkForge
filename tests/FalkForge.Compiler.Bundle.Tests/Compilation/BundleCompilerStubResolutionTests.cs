using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Verifies the bundle compiler's stub policy: by default the REAL resolved engine binary is
/// embedded as the bundle's PE front (the output is a runnable self-extracting installer), an
/// unresolvable engine FAILS the build loud instead of silently degrading, and the design-time
/// placeholder is available only through the explicit <c>AllowPlaceholderStub</c> opt-in. The
/// embedded engine performs the install and the runtime trust verification, so a silent
/// placeholder default would ship bundles that verify but cannot install.
/// </summary>
public sealed class BundleCompilerStubResolutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _payloadPath;

    public BundleCompilerStubResolutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleStubRes_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _payloadPath = Path.Combine(_tempDir, "payload.msi");
        File.WriteAllBytes(_payloadPath, [0xD0, 0xCF, 0x11, 0xE0, 0x00]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private BundleModel BuildModel(string name = "StubResBundle") => new()
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
                DisplayName = "Payload"
            }
        }.AsReadOnly()
    };

    private string WriteFakeEngine(string fileName = "fake-engine.exe")
    {
        var path = Path.Combine(_tempDir, fileName);
        var bytes = new byte[256];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        // Distinctive marker so tests can prove THESE bytes ended up as the bundle's front.
        bytes[2] = 0xFA;
        bytes[3] = 0x1C;
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] ReadPrefix(string path, int count)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    // ── default path: the resolved engine is embedded ────────────────────────

    [Fact]
    public void Compile_DefaultPath_EmbedsResolvedEngineAsPeFront()
    {
        var engine = WriteFakeEngine();
        var compiler = new BundleCompiler
        {
            EngineStubResolver = () => Result<string>.Success(engine)
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-default"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var prefix = ReadPrefix(result.Value, 4);
        Assert.Equal((byte)'M', prefix[0]);
        Assert.Equal((byte)'Z', prefix[1]);
        Assert.Equal(0xFA, prefix[2]);
        Assert.Equal(0x1C, prefix[3]);

        // The bundle must still be readable — the payload TOC survives a real PE front.
        var content = PayloadEmbedder.Extract(result.Value);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        Assert.Single(content.Value.TocEntries);
    }

    [Fact]
    public void Compile_DefaultPath_EngineUnresolvable_FailsLoud_NoSilentPlaceholder()
    {
        var compiler = new BundleCompiler
        {
            EngineStubResolver = () => Result<string>.Failure(
                ErrorKind.BundleError, "engine not found (test)")
        };
        var outDir = Path.Combine(_tempDir, "out-fail");

        var result = compiler.Compile(BuildModel(), outDir);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("engine not found (test)", result.Error.Message, StringComparison.Ordinal);
        // No half-built bundle exe may be left behind on the failure path.
        Assert.False(File.Exists(Path.Combine(outDir, "StubResBundle.exe")));
    }

    // ── explicit EngineStubPath ──────────────────────────────────────────────

    [Fact]
    public void Compile_ExplicitEngineStubPath_TakesPrecedenceOverResolver()
    {
        var engine = WriteFakeEngine();
        var compiler = new BundleCompiler
        {
            EngineStubPath = engine,
            EngineStubResolver = () => Result<string>.Failure(
                ErrorKind.BundleError, "resolver must not be consulted")
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-explicit"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal((byte)'M', ReadPrefix(result.Value, 2)[0]);
    }

    [Fact]
    public void Compile_ExplicitEngineStubPathMissing_FailsLoud()
    {
        // Previously a missing explicit stub silently degraded to the empty placeholder —
        // the operator asked for a specific engine and must get it or an error.
        var compiler = new BundleCompiler
        {
            EngineStubPath = Path.Combine(_tempDir, "no-such-stub.exe")
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-missing"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("EngineStubPath", result.Error.Message, StringComparison.Ordinal);
    }

    // ── explicit placeholder opt-in ──────────────────────────────────────────

    [Fact]
    public void Compile_AllowPlaceholderStub_ProducesDesignTimeBundleStartingWithMagic()
    {
        var compiler = new BundleCompiler { AllowPlaceholderStub = true };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-placeholder"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        // Placeholder stub is empty, so the file begins directly with the bundle magic.
        var magic = PayloadEmbedder.BundleMagic.ToArray();
        Assert.Equal(magic, ReadPrefix(result.Value, magic.Length));
    }

    [Fact]
    public void Compile_AllowPlaceholderStub_NeverConsultsAmbientResolution()
    {
        // The opt-in must make the build hermetic: no machine state (env var, published
        // artifacts) may leak a multi-megabyte engine into a deliberately-stubless bundle.
        var resolverConsulted = false;
        var compiler = new BundleCompiler
        {
            AllowPlaceholderStub = true,
            EngineStubResolver = () =>
            {
                resolverConsulted = true;
                return Result<string>.Success(WriteFakeEngine("ambient.exe"));
            }
        };

        var result = compiler.Compile(BuildModel(), Path.Combine(_tempDir, "out-hermetic"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.False(resolverConsulted);
    }

    // ── DeltaBundleCompiler mirrors the same policy ──────────────────────────

    [Fact]
    public void DeltaCompile_DefaultPath_EngineUnresolvable_FailsLoud()
    {
        // Build the base bundle with the explicit placeholder opt-in, then require the delta
        // compile to fail when no engine resolves — same policy as the full compiler.
        var baseCompiler = new BundleCompiler { AllowPlaceholderStub = true };
        var baseResult = baseCompiler.Compile(BuildModel("DeltaBase"), Path.Combine(_tempDir, "delta-base"));
        Assert.True(baseResult.IsSuccess, baseResult.IsFailure ? baseResult.Error.Message : null);

        var deltaCompiler = new DeltaBundleCompiler
        {
            EngineStubResolver = () => Result<string>.Failure(
                ErrorKind.BundleError, "engine not found (delta test)")
        };

        var result = deltaCompiler.Compile(
            BuildModel("DeltaNew"), Path.Combine(_tempDir, "delta-out"), baseResult.Value);

        Assert.True(result.IsFailure);
        Assert.Contains("engine not found (delta test)", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeltaCompile_AllowPlaceholderStub_StillProducesDesignTimeBundle()
    {
        var baseCompiler = new BundleCompiler { AllowPlaceholderStub = true };
        var baseResult = baseCompiler.Compile(BuildModel("DeltaBase2"), Path.Combine(_tempDir, "delta-base2"));
        Assert.True(baseResult.IsSuccess, baseResult.IsFailure ? baseResult.Error.Message : null);

        var deltaCompiler = new DeltaBundleCompiler { AllowPlaceholderStub = true };
        var result = deltaCompiler.Compile(
            BuildModel("DeltaNew2"), Path.Combine(_tempDir, "delta-out2"), baseResult.Value);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var magic = PayloadEmbedder.BundleMagic.ToArray();
        Assert.Equal(magic, ReadPrefix(result.Value, magic.Length));
    }
}
