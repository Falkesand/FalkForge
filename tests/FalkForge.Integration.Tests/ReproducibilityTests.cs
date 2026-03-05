using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using Xunit;

namespace FalkForge.Integration.Tests;

[SupportedOSPlatform("windows")]
public sealed class ReproducibilityTests
{
    // Fixed Unix epoch: 2020-01-01T00:00:00Z = 1577836800
    private const long TestEpoch = 1577836800L;

    [Fact]
    public void Reproducible_MsiBuiltTwice_ProducesIdenticalOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-repro-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // Create a fake payload with a pinned LastWriteTime so the test is hermetic
            var payloadPath = Path.Combine(tempDir, "payload.dll");
            File.WriteAllBytes(payloadPath, new byte[] { 0x4D, 0x5A, 0x01, 0x02, 0x03, 0x04 });
            File.SetLastWriteTimeUtc(payloadPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var outputDir1 = Path.Combine(tempDir, "out1");
            var outputDir2 = Path.Combine(tempDir, "out2");

            var (hash1, msiPath1) = BuildAndHashWithPath(payloadPath, outputDir1, reproducible: true);
            var (hash2, msiPath2) = BuildAndHashWithPath(payloadPath, outputDir2, reproducible: true);

            if (hash1 != hash2)
            {
                var b1 = File.ReadAllBytes(msiPath1);
                var b2 = File.ReadAllBytes(msiPath2);
                var minLen = Math.Min(b1.Length, b2.Length);
                var diffMsg = $"Sizes: {b1.Length} vs {b2.Length}. ";
                for (var i = 0; i < minLen; i++)
                {
                    if (b1[i] != b2[i])
                    {
                        var start = Math.Max(0, i - 4);
                        var end = Math.Min(minLen, i + 20);
                        var ctx1 = Convert.ToHexString(b1[start..end]);
                        var ctx2 = Convert.ToHexString(b2[start..end]);
                        diffMsg += $"First diff at offset 0x{i:X4} ({i}): 0x{b1[i]:X2} vs 0x{b2[i]:X2}. Context: [{ctx1}] vs [{ctx2}]";
                        break;
                    }
                }
                Assert.Fail($"MSI hashes differ. {diffMsg}");
            }

            Assert.Equal(hash1, hash2);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void NonReproducible_MsiBuiltTwice_ProducesDifferentOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"falk-nonrepro-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var payloadPath = Path.Combine(tempDir, "payload.dll");
            File.WriteAllBytes(payloadPath, new byte[] { 0x4D, 0x5A, 0x01, 0x02, 0x03, 0x04 });
            File.SetLastWriteTimeUtc(payloadPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var outputDir1 = Path.Combine(tempDir, "out1");
            var outputDir2 = Path.Combine(tempDir, "out2");

            var hash1 = BuildAndHash(payloadPath, outputDir1, reproducible: false);
            var hash2 = BuildAndHash(payloadPath, outputDir2, reproducible: false);

            // Without reproducible mode the ProductCode is a fresh Guid each build,
            // which propagates into the MSI bytes and makes hashes differ.
            Assert.NotEqual(hash1, hash2);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static (string Hash, string MsiPath) BuildAndHashWithPath(string payloadPath, string outputDir, bool reproducible)
    {
        var msiPath = BuildMsi(payloadPath, outputDir, reproducible);
        return (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(msiPath))), msiPath);
    }

    private static string BuildAndHash(string payloadPath, string outputDir, bool reproducible)
    {
        var msiPath = BuildMsi(payloadPath, outputDir, reproducible);
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(msiPath)));
    }

    private static string BuildMsi(string payloadPath, string outputDir, bool reproducible)
    {
        var builder = new PackageBuilder
        {
            Name = "ReproTest",
            Manufacturer = "FalkForge Tests",
            Version = new Version(1, 0, 0),
        };

        builder.Files(f => f
            .Add(payloadPath)
            .To(KnownFolder.ProgramFiles / "FalkForge Tests" / "ReproTest"));

        if (reproducible)
            builder.Reproducible(TestEpoch);

        var model = builder.Build();
        var compiler = new MsiCompiler();
        var result = compiler.Compile(model, outputDir);

        Assert.True(result.IsSuccess, $"Compilation failed: {(result.IsFailure ? result.Error.Message : string.Empty)}");
        Assert.True(File.Exists(result.Value), $"Output MSI not found: {result.Value}");

        return result.Value;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
    }
}
