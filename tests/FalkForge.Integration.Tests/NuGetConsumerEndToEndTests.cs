using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Proves the packaged consumer story end to end: someone with NO repo checkout and NO publish
/// output — only the NuGet packages produced by <c>scripts/pack.ps1</c> — gets a RUNNABLE bundle
/// with zero manual engine setup.
/// <para>
/// Three consumer-facing properties are pinned:
/// </para>
/// <list type="number">
///   <item><description>The <c>FalkForge.Engine.Runtime.win-x64</c> package genuinely carries the
///   published NativeAOT engine and elevation companion (real PE binaries, not placeholders),
///   plus the build props that surface them to MSBuild consumers.</description></item>
///   <item><description>Installing <c>FalkForge.Tool</c> from a local feed into a clean tool path
///   and running <c>forge build</c> on a minimal signed-bundle config produces a bundle whose PE
///   front is the real engine and which genuinely self-extracts.</description></item>
///   <item><description>The runtime package's build props copy the engine into a consumer
///   project's <c>$(OutDir)engine\</c>, which is exactly where
///   <see cref="EngineStubLocator"/>'s beside-host probe already looks — the code-first
///   <c>dotnet exec … --forge-build</c> flow resolves it with no environment variable.</description></item>
/// </list>
/// <para>
/// All tests are gated on the local feed produced by <c>scripts/pack.ps1</c> (which publishes the
/// NativeAOT engine first and packs it into the packages). The gate is explicit
/// (<c>Assert.SkipUnless</c>) because the feed requires a multi-minute NativeAOT publish.
/// </para>
/// </summary>
public sealed class NuGetConsumerEndToEndTests : IDisposable
{
    private const string RuntimePackageId = "FalkForge.Engine.Runtime.win-x64";
    private const string ToolPackageId = "FalkForge.Tool";

    private readonly string _tempDir;

    public NuGetConsumerEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkNuGetE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: a straggling tool process may still hold a handle briefly.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort rationale as above.
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    /// <summary>
    /// The local feed produced by <c>scripts/pack.ps1</c>, or null when it (or either package the
    /// tests consume) is absent. Null gates every test with an explicit skip.
    /// </summary>
    private static (string Feed, string RuntimeNupkg, string ToolNupkg)? FindLocalFeed()
    {
        var root = FindRepoRoot();
        if (root is null)
            return null;

        var feed = Path.Combine(root, "artifacts", "nuget");
        if (!Directory.Exists(feed))
            return null;

        var runtime = Directory.GetFiles(feed, RuntimePackageId + ".*.nupkg").SingleOrDefault();
        var tool = Directory.GetFiles(feed, ToolPackageId + ".*.nupkg").SingleOrDefault();
        if (runtime is null || tool is null)
            return null;

        return (feed, runtime, tool);
    }

    private const string FeedSkipReason =
        "Local NuGet feed with " + ToolPackageId + " and " + RuntimePackageId + " not found at " +
        "artifacts/nuget — run scripts/pack.ps1 first. This gate exists because packing requires " +
        "the multi-minute NativeAOT engine publish.";

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName, string[] arguments, IDictionary<string, string>? environment = null,
        string? workingDirectory = null, int timeoutMinutes = 5)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;
        if (environment is not null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(timeoutMinutes)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{Path.GetFileName(fileName)} did not exit within {timeoutMinutes} minutes. " +
                        $"stdout: {stdout} stderr: {stderr}");
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static void AssertRealEngineBinary(string path, string what)
    {
        Assert.True(File.Exists(path), $"expected {what} at {path}");

        var info = new FileInfo(path);
        Assert.True(info.Length > 1024 * 1024,
            $"{what} is {info.Length:N0} bytes — far too small to be the NativeAOT binary. " +
            "A placeholder must never ship.");

        using var stream = File.OpenRead(path);
        var prefix = new byte[2];
        stream.ReadExactly(prefix);
        Assert.Equal((byte)'M', prefix[0]);
        Assert.Equal((byte)'Z', prefix[1]);
    }

    [Fact]
    public void EngineRuntimePackage_CarriesPublishedEngineAndCompanion_AsRealPeBinaries()
    {
        var feed = FindLocalFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        using var archive = ZipFile.OpenRead(feed.Value.RuntimeNupkg);

        // The engine and companion travel under tools/engine — a folder NuGet never wires into
        // consumer compilation, so the exes are payload, not references.
        foreach (var name in new[] { "FalkForge.Engine.exe", "FalkForge.Engine.Elevation.exe" })
        {
            var entry = archive.GetEntry($"tools/engine/{name}");
            Assert.True(entry is not null, $"package must contain tools/engine/{name}");
            Assert.True(entry!.Length > 1024 * 1024,
                $"tools/engine/{name} is {entry.Length:N0} bytes — far too small to be the " +
                "published NativeAOT binary. A placeholder must never ship.");

            using var stream = entry.Open();
            var prefix = new byte[2];
            stream.ReadExactly(prefix);
            Assert.Equal((byte)'M', prefix[0]);
            Assert.Equal((byte)'Z', prefix[1]);
        }

        // The build props are the consumer-facing surface: they copy the binaries beside the
        // consumer's build output where EngineStubLocator's beside-host probe finds them.
        // buildTransitive/ makes the props flow to projects that get the package transitively.
        Assert.NotNull(archive.GetEntry($"build/{RuntimePackageId}.props"));
        Assert.NotNull(archive.GetEntry($"buildTransitive/{RuntimePackageId}.props"));
    }

    [Fact]
    public void ToolInstalledFromLocalFeed_ForgeBuild_ProducesRunnableSelfExtractingBundle()
    {
        var feed = FindLocalFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        // Isolated NuGet state: a private package cache and a config whose ONLY source is the
        // local feed. Without the private cache a previously cached FalkForge.Tool of the same
        // version would shadow the freshly packed one and the test would prove nothing.
        var nugetCache = Path.Combine(_tempDir, "nuget-cache");
        var toolPath = Path.Combine(_tempDir, "tools");
        var nugetConfig = Path.Combine(_tempDir, "nuget.config");
        File.WriteAllText(nugetConfig, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="falkforge-local" value="{feed.Value.Feed}" />
              </packageSources>
            </configuration>
            """);

        var environment = new Dictionary<string, string> { ["NUGET_PACKAGES"] = nugetCache };
        var (installExit, installOut, installErr) = RunProcess("dotnet",
            ["tool", "install", ToolPackageId, "--tool-path", toolPath, "--prerelease",
             "--configfile", nugetConfig],
            environment, timeoutMinutes: 10);
        Assert.True(installExit == 0,
            $"dotnet tool install failed (exit {installExit}). stdout: {installOut} stderr: {installErr}");

        // The engine and companion must have landed inside the installed tool's directory — the
        // exact "engine subdirectory beside the host" location EngineStubLocator probes.
        var installedEngines = Directory.GetFiles(toolPath, "FalkForge.Engine.exe", SearchOption.AllDirectories);
        var installedEngine = Assert.Single(installedEngines);
        Assert.Equal("engine", Path.GetFileName(Path.GetDirectoryName(installedEngine)));
        AssertRealEngineBinary(installedEngine, "installed tool's engine");
        AssertRealEngineBinary(
            Path.Combine(Path.GetDirectoryName(installedEngine)!, "FalkForge.Engine.Elevation.exe"),
            "installed tool's elevation companion");

        // Minimal consumer project: one payload file, pem signing (signing is what routes forge
        // build into the bundle path). The key is generated here — a NuGet consumer needs no
        // FalkForge-provided key material.
        var projectDir = Path.Combine(_tempDir, "project");
        Directory.CreateDirectory(Path.Combine(projectDir, "payload"));

        var payloadBytes = new byte[64 * 1024];
        Random.Shared.NextBytes(payloadBytes);
        File.WriteAllBytes(Path.Combine(projectDir, "payload", "app.exe"), payloadBytes);

        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            File.WriteAllText(Path.Combine(projectDir, "key.pem"), key.ExportECPrivateKeyPem());

        File.WriteAllText(Path.Combine(projectDir, "installer.json"), """
            {
              "product": {
                "name": "FeedConsumerApp",
                "manufacturer": "Contoso",
                "version": "1.0.0"
              },
              "ui": "Minimal",
              "features": [
                {
                  "id": "Complete",
                  "title": "Complete",
                  "files": [
                    { "source": "payload/app.exe" }
                  ]
                }
              ],
              "signing": {
                "provider": "pem",
                "keyPath": "key.pem"
              }
            }
            """);

        // forge build with NOTHING but the installed tool: no FALKFORGE_ENGINE_STUB, no repo
        // walk-up (the working directory is outside any FalkForge checkout).
        var outputDir = Path.Combine(_tempDir, "out");
        var forgeExe = Path.Combine(toolPath, "forge.exe");
        var (buildExit, buildOut, buildErr) = RunProcess(forgeExe,
            ["build", Path.Combine(projectDir, "installer.json"), "--output", outputDir, "--no-ice"],
            workingDirectory: projectDir, timeoutMinutes: 5);
        Assert.True(buildExit == 0,
            $"forge build failed (exit {buildExit}). stdout: {buildOut} stderr: {buildErr}");

        var bundlePath = Assert.Single(Directory.GetFiles(outputDir, "*.exe"));
        var msiPath = Assert.Single(Directory.GetFiles(outputDir, "*.msi"));

        // The bundle's PE front is the real engine: MZ header, and strictly larger than the
        // engine it embeds (engine + manifest + compressed payloads + TOC).
        using (var stream = File.OpenRead(bundlePath))
        {
            var prefix = new byte[2];
            stream.ReadExactly(prefix);
            Assert.Equal((byte)'M', prefix[0]);
            Assert.Equal((byte)'Z', prefix[1]);
            Assert.True(stream.Length > new FileInfo(installedEngine).Length,
                "bundle must be strictly larger than the engine it embeds");
        }

        // The produced exe genuinely self-extracts: the running stub is the real engine reading
        // its own embedded TOC and writing the chained MSI back out byte-for-byte.
        var (listExit, listOut, listErr) = RunProcess(bundlePath, ["--extract-list"]);
        Assert.True(listExit == 0, $"--extract-list failed (exit {listExit}). stderr: {listErr}");
        Assert.Contains("MainMsi", listOut, StringComparison.Ordinal);

        var extractDir = Path.Combine(_tempDir, "extracted");
        var (extractExit, _, extractErr) = RunProcess(bundlePath, ["--extract", extractDir]);
        Assert.True(extractExit == 0, $"--extract failed (exit {extractExit}). stderr: {extractErr}");

        var extractedMsi = Path.Combine(extractDir, "MainMsi", "MainMsi.dat");
        Assert.True(File.Exists(extractedMsi), $"expected extracted MSI at {extractedMsi}");
        Assert.Equal(File.ReadAllBytes(msiPath), File.ReadAllBytes(extractedMsi));
    }

    [Fact]
    public void EngineRuntimePackageProps_LandEngineWhereBesideHostProbeResolves()
    {
        var feed = FindLocalFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        // Consume the runtime package the way MSBuild would after restore: the extracted package
        // folder with its build props imported into a plain consumer project. (The full
        // NuGet-restore SDK-consumer e2e is tracked as a follow-up; this pins the package's
        // MSBuild contract — the props must land the binaries where the locator already looks.)
        var packageDir = Path.Combine(_tempDir, "package");
        ZipFile.ExtractToDirectory(feed.Value.RuntimeNupkg, packageDir);

        var consumerDir = Path.Combine(_tempDir, "consumer");
        Directory.CreateDirectory(consumerDir);
        var propsPath = Path.Combine(packageDir, "build", RuntimePackageId + ".props");
        File.WriteAllText(Path.Combine(consumerDir, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <Import Project="{propsPath}" />
            </Project>
            """);
        File.WriteAllText(Path.Combine(consumerDir, "Program.cs"),
            """Console.WriteLine("consumer");""");

        var (buildExit, buildOut, buildErr) = RunProcess("dotnet",
            ["build", Path.Combine(consumerDir, "Consumer.csproj"), "-c", "Release"],
            timeoutMinutes: 5);
        Assert.True(buildExit == 0,
            $"consumer build failed (exit {buildExit}). stdout: {buildOut} stderr: {buildErr}");

        var binDir = Path.Combine(consumerDir, "bin", "Release", "net10.0");
        var engineInOutput = Path.Combine(binDir, "engine", "FalkForge.Engine.exe");
        AssertRealEngineBinary(engineInOutput, "engine copied to consumer output");
        AssertRealEngineBinary(Path.Combine(binDir, "engine", "FalkForge.Engine.Elevation.exe"),
            "elevation companion copied to consumer output");

        // The landed layout IS a location the bundle compiler's default resolution probes: with
        // the consumer's output directory as the host base directory (the dotnet exec
        // --forge-build situation) and no environment variable, the locator finds this engine.
        var resolved = EngineStubLocator.Resolve(
            environmentValue: null, baseDirectory: binDir, currentDirectory: null);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Message : null);
        Assert.Equal(Path.GetFullPath(engineInOutput), Path.GetFullPath(resolved.Value));
    }
}
