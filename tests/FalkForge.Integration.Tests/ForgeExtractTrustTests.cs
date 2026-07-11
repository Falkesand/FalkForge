using System.Security.Cryptography;
using FalkForge.Cli;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// C14 Stage 2 / §1.4: <c>forge extract</c> previously extracted signed-bundle payloads with no trust
/// binding — it verified each payload only against the (unsigned, attacker-controllable) overlay TOC
/// hash. So a validly signed bundle whose payload bytes + TOC hash were rewritten after signing would
/// extract the tampered bytes as if trusted. Stage 2 routes the CLI through the shared byte→TOC→signed
/// binding, so a tampered signed bundle fails loud and no tampered file is written (inspection-grade
/// trust: the CLI has no baked pin, but the binding + coverage still catch post-signing tampering).
/// </summary>
public sealed class ForgeExtractTrustTests
{
    [Fact]
    public void ForgeExtract_TamperedSignedBundle_FailsLoud_NoTamperedFileWritten()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"falk-extract-trust-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 1. Build a validly integrity-signed bundle over the original payload.
            var originalBytes = RandomNumberGenerator.GetBytes(256);
            var msiPath = Path.Combine(dir, "App.msi");
            File.WriteAllBytes(msiPath, originalBytes);

            var model = new BundleBuilder()
                .Name("ExtractTrust")
                .Manufacturer("Integration Tests")
                .Version("1.0.0")
                .UseSilentUI()
                .Integrity(i => { })
                .Chain(chain => chain.MsiPackage(msiPath, pkg => pkg.Id("AppMsi").Version("1.0.0")))
                .Build();

            var buildResult = new BundleCompiler { AllowPlaceholderStub = true }.Compile(model, Path.Combine(dir, "out"));
            Assert.True(buildResult.IsSuccess, buildResult.IsFailure ? buildResult.Error.Message : null);

            var signedContent = PayloadEmbedder.Extract(buildResult.Value);
            Assert.True(signedContent.IsSuccess, signedContent.IsFailure ? signedContent.Error.Message : null);
            var signedManifest = DeserializeManifest(signedContent.Value);

            // 2. Attacker: tamper the payload bytes and re-embed with the UNCHANGED signed manifest but a
            //    TOC hash matching the tampered bytes (a post-signing overlay rewrite).
            var tamperedBytes = (byte[])originalBytes.Clone();
            tamperedBytes[0] ^= 0xFF;
            var tamperedMsi = Path.Combine(dir, "App.tampered.msi");
            File.WriteAllBytes(tamperedMsi, tamperedBytes);
            var tamperedHash = Convert.ToHexString(SHA256.HashData(tamperedBytes));

            var stubPath = Path.Combine(dir, "stub.bin");
            File.WriteAllBytes(stubPath, []);
            var attackerBundle = Path.Combine(dir, "attacker.exe");

            var tamperedPayload = new PayloadEntry
            {
                PackageId = "AppMsi",
                SourcePath = tamperedMsi,
                OriginalSize = tamperedBytes.Length,
                Sha256Hash = tamperedHash
            };

            var embed = new PayloadEmbedder().Embed(stubPath, attackerBundle, signedManifest, new[] { tamperedPayload });
            Assert.True(embed.IsSuccess, embed.IsFailure ? embed.Error.Message : null);

            // 3a. Positive control: the CLEAN signed bundle extracts successfully and writes its payload.
            //     This proves the command harness + args work, so the tampered failure below cannot be
            //     a spurious "everything errors" green.
            var cleanOut = Path.Combine(dir, "clean-extracted");
            var cleanExit = RunForgeExtract(buildResult.Value, cleanOut);
            var cleanFiles = Directory.Exists(cleanOut)
                ? Directory.GetFiles(cleanOut, "*", SearchOption.AllDirectories).Length : 0;

            // 3b. forge extract on the tampered signed bundle must fail loud and write nothing.
            var outputDir = Path.Combine(dir, "extracted");
            var exit = RunForgeExtract(attackerBundle, outputDir);
            var tamperedFiles = Directory.Exists(outputDir)
                ? Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories).Length : 0;

            Assert.True(cleanExit == ExitCodes.Success,
                $"clean control: expected Success, got exit {cleanExit}, cleanFiles={cleanFiles}");
            Assert.True(cleanFiles > 0, "clean control: expected at least one extracted file");
            Assert.True(exit != ExitCodes.Success,
                $"tampered: expected failure, got exit {exit}, tamperedFiles={tamperedFiles}");
            Assert.True(tamperedFiles == 0,
                $"tampered: expected no files written, got {tamperedFiles}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static int RunForgeExtract(string bundlePath, string outputDir)
    {
        // Invoke the real ExtractCommand against a silent console (the established Cli.Tests pattern:
        // Spectre 0.55 made Execute protected, reachable via ICommand<T>.ExecuteAsync). This drives the
        // actual `forge extract` bundle path, not a re-implementation.
        var command = new ExtractCommand(new SilentConsole());
        var settings = new ExtractSettings { FilePath = bundlePath, OutputPath = outputDir };
        var context = new CommandContext([], new EmptyRemaining(), "extract", null);
        return ((ICommand<ExtractSettings>)command)
            .ExecuteAsync(context, settings, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    private sealed class SilentConsole : IConsoleOutput
    {
        public void MarkupLine(string markup) { }
        public void WriteLine(string text) { }
        public void WriteError(string text) { }
    }

    private sealed class EmptyRemaining : IRemainingArguments
    {
        public IReadOnlyList<string> Raw => [];
        public ILookup<string, string?> Parsed =>
            Array.Empty<string>().ToLookup(_ => string.Empty, _ => (string?)null);
    }

    private static InstallerManifest DeserializeManifest(BundleContent content)
    {
        var manifest = System.Text.Json.JsonSerializer.Deserialize<InstallerManifest>(content.ManifestJsonBytes!);
        Assert.NotNull(manifest);
        return manifest!;
    }
}
