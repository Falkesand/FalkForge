using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Characterization tests for <see cref="SelfExtractionMode"/>, extracted (pure move) out of
/// <c>Program.Main</c>'s inline <c>--extract</c>/<c>--extract-list</c> block. These pin the exit
/// codes and output the original inline code produced, proving the move changed nothing.
/// <para>
/// Bundles are built with <c>AllowPlaceholderStub = true</c> (same helper pattern as
/// <see cref="ForgeExtractTrustTests"/> / <see cref="HybridBundleFluentEndToEndTests"/>) — a real
/// FALKBUNDLE-format file readable by <c>BundleReader.Extract</c>, but far cheaper than embedding
/// the published NativeAOT engine. Tests call <see cref="SelfExtractionMode.RunAsync(EngineInvocationArgs, string?)"/>
/// directly with the built bundle's path as the <c>selfPathOverride</c> test seam, since the test
/// host process is never itself a self-extracting bundle (<c>Environment.ProcessPath</c> can't be
/// spoofed to point at one).
/// </para>
/// </summary>
public sealed class SelfExtractionModeTests : IDisposable
{
    private readonly string _tempDir;

    public SelfExtractionModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SelfExtractionMode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static EngineInvocationArgs MakeArgs(
        string? extractDir = null,
        bool extractList = false,
        IReadOnlyList<string>? extractPackages = null) =>
        new(
            PipeName: null,
            SecretPipeName: null,
            ManifestPath: null,
            PlanOnly: false,
            PlanOutputPath: null,
            SbomOutputPath: null,
            ExtractDir: extractDir,
            ExtractList: extractList,
            ExtractPackages: extractPackages ?? Array.Empty<string>(),
            BaseBundlePath: null,
            RequireSigned: false);

    private string BuildUnsignedBundle(byte[] payloadBytes, string packageId)
    {
        var payloadPath = Path.Combine(_tempDir, $"{packageId}.msi");
        File.WriteAllBytes(payloadPath, payloadBytes);

        var model = new BundleBuilder()
            .Name("SelfExtractionModeTest")
            .Manufacturer("Integration Tests")
            .Version("1.0.0")
            .UseSilentUI()
            .Integrity(i => { })
            .Chain(chain => chain.MsiPackage(payloadPath, pkg => pkg.Id(packageId).Version("1.0.0")))
            .Build();

        var buildResult = new BundleCompiler { AllowPlaceholderStub = true }
            .Compile(model, Path.Combine(_tempDir, $"out-{packageId}"));
        Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);
        return buildResult.Value;
    }

    [Fact]
    public async Task RunAsync_ExtractsPayload_ByteForByte_ExitZero()
    {
        var payloadBytes = RandomNumberGenerator.GetBytes(4096);
        var bundlePath = BuildUnsignedBundle(payloadBytes, "AppMsi");
        var extractDir = Path.Combine(_tempDir, "extracted");

        var exitCode = await SelfExtractionMode.RunAsync(MakeArgs(extractDir: extractDir), bundlePath);

        Assert.Equal(0, exitCode);
        var extractedFile = Path.Combine(extractDir, "AppMsi", "AppMsi.dat");
        Assert.True(File.Exists(extractedFile), $"expected extracted payload at {extractedFile}");
        Assert.Equal(payloadBytes, File.ReadAllBytes(extractedFile));
    }

    [Fact]
    public async Task RunAsync_ExtractList_PrintsToc_ExitZero()
    {
        var bundlePath = BuildUnsignedBundle(RandomNumberGenerator.GetBytes(256), "ListedMsi");

        var originalOut = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        int exitCode;
        try
        {
            exitCode = await SelfExtractionMode.RunAsync(MakeArgs(extractList: true), bundlePath);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, exitCode);
        Assert.Contains("ListedMsi", sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PackageFilterMissingPackage_ExitOne()
    {
        var bundlePath = BuildUnsignedBundle(RandomNumberGenerator.GetBytes(256), "RealMsi");
        var extractDir = Path.Combine(_tempDir, "extracted-filtered");

        var originalErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        int exitCode;
        try
        {
            exitCode = await SelfExtractionMode.RunAsync(
                MakeArgs(extractDir: extractDir, extractPackages: new[] { "NoSuchPackage" }), bundlePath);
        }
        finally
        {
            Console.SetError(originalErr);
        }

        Assert.Equal(1, exitCode);
        Assert.Contains("NoSuchPackage", sw.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(extractDir, "RealMsi")));
    }

    [Fact]
    public async Task RunAsync_CorruptBundle_ExitTwo()
    {
        var bundlePath = BuildUnsignedBundle(RandomNumberGenerator.GetBytes(256), "CorruptMsi");

        // Corrupt the FALKBUNDLE footer magic (the last 24 bytes) so BundleReader.Extract's
        // TryLocateFooter fails to recognize the file — the "not a valid bundle" branch, exit 2.
        var corruptPath = Path.Combine(_tempDir, "corrupt.exe");
        File.Copy(bundlePath, corruptPath);
        using (var stream = new FileStream(corruptPath, FileMode.Open, FileAccess.ReadWrite))
        {
            // Footer layout is magic(16) + tocOffset(int64, 8) = last 24 bytes. Flip the first
            // byte of the magic so TryLocateFooter's magic comparison fails outright.
            stream.Seek(-24, SeekOrigin.End);
            stream.WriteByte(0xFF);
        }

        var extractDir = Path.Combine(_tempDir, "extracted-corrupt");
        var exitCode = await SelfExtractionMode.RunAsync(MakeArgs(extractDir: extractDir), corruptPath);

        Assert.Equal(2, exitCode);
    }
}
