using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[SupportedOSPlatform("windows")]
public sealed class DemoBuildFixture : IDisposable
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, DemoBuildResult> _cache = new();
    private readonly ConcurrentBag<string> _filesToCleanup = [];
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), $"falk-demo-e2e-{Guid.NewGuid():N}");
    private readonly PayloadProvisioner _provisioner = new();

    private readonly string _engineStubPath;

    public DemoBuildFixture()
    {
        Directory.CreateDirectory(_tempRoot);

        // Bundle demos run with the production default: the compiler resolves a REAL engine and
        // fails loud when none is found. The harness plays the operator's role by provisioning
        // an engine stub and pointing FALKFORGE_ENGINE_STUB at it, keeping the demo sources on
        // the honest default path without requiring a multi-minute NativeAOT publish per test
        // run. A minimal MZ file suffices here — these tests assert the demo BUILD produces its
        // artifact; RunnableBundleEndToEndTests proves self-extraction against the real engine.
        var engineStubDir = Path.Combine(_tempRoot, "engine-stub");
        Directory.CreateDirectory(engineStubDir);
        _engineStubPath = Path.Combine(engineStubDir, "FalkForge.Engine.exe");
        var stubBytes = new byte[512];
        stubBytes[0] = (byte)'M';
        stubBytes[1] = (byte)'Z';
        File.WriteAllBytes(_engineStubPath, stubBytes);

        // Mirror the publish layout: the elevation companion lives beside the engine. A runnable
        // bundle embeds it as a trust-covered payload by default, so an engine without a companion
        // beside it would (deliberately) fail the demo build loud.
        File.WriteAllBytes(
            Path.Combine(engineStubDir, Engine.Protocol.Bundle.EngineCompanionPayload.PackageId),
            [(byte)'M', (byte)'Z', 0xE1, 0xE7]);

        // Force-killed test runs skip Dispose, leaving falk-demo-e2e-* roots in %TEMP%.
        // Self-heal on next run: delete sibling roots older than 24 hours (best-effort, never throw).
        try
        {
            var tempPath = Path.GetTempPath();
            foreach (var dir in Directory.EnumerateDirectories(tempPath, "falk-demo-e2e-*"))
            {
                if (dir == _tempRoot)
                    continue;

                try
                {
                    var created = Directory.GetCreationTimeUtc(dir);
                    if (DateTime.UtcNow - created > TimeSpan.FromHours(24))
                        Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // Best effort per directory — locked handles, permissions, etc.
                }
            }
        }
        catch
        {
            // Never throw from constructor.
        }
    }

    public DemoBuildResult GetOrBuild(DemoExpectation demo) =>
        _cache.GetOrAdd(demo.Name, _ => Build(demo));

    private DemoBuildResult Build(DemoExpectation demo)
    {
        // Provision any stub payload files the demo needs (e.g. MyApp.msi for bundle demos).
        // Provisioner returns paths it just created; register them for cleanup.
        var newStubs = _provisioner.Provision(demo);
        foreach (var path in newStubs)
            _filesToCleanup.Add(path);

        // Bundle demos reference sibling MSI projects via relative paths (e.g., ../app-installer/app-installer.msi).
        // For bundles, we must first ensure their MSI dependencies are built into the expected locations.
        if (demo.OutputType == DemoOutputType.Bundle)
            EnsureBundleDependencies(demo);

        var outputDir = Path.Combine(_tempRoot, demo.Name.Replace('/', '_'));
        Directory.CreateDirectory(outputDir);
        return RunProcess(demo, outputDir);
    }

    private DemoBuildResult RunProcess(DemoExpectation demo, string outputDir)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{demo.ProjectPath}\" -- -o \"{outputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(demo.ProjectPath)!,
        };

        // Demos using .Reproducible() require SOURCE_DATE_EPOCH
        process.StartInfo.Environment["SOURCE_DATE_EPOCH"] = "1577836800";

        // Disable MSBuild node reuse so worker nodes don't linger between test runs.
        // With nodeReuse:true (the default), cross-SDK orphan nodes accumulate on machines
        // with many SDK installations and degrade subsequent builds. CI impact is minimal.
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        // Deterministic engine resolution for bundle demos (see constructor comment).
        process.StartInfo.Environment["FALKFORGE_ENGINE_STUB"] = _engineStubPath;

        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)BuildTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }

            return new DemoBuildResult(
                ExitCode: -1,
                OutputFile: null,
                OutputDir: outputDir,
                Stdout: stdout.Result,
                Stderr: $"Process timed out after {BuildTimeout.TotalSeconds}s\n{stderr.Result}");
        }

        stdout.Wait();
        stderr.Wait();

        var outputFile = FindOutputFile(outputDir, demo.OutputType);

        return new DemoBuildResult(
            ExitCode: process.ExitCode,
            OutputFile: outputFile,
            OutputDir: outputDir,
            Stdout: stdout.Result,
            Stderr: stderr.Result);
    }

    private static string? FindOutputFile(string dir, DemoOutputType outputType)
    {
        var pattern = outputType switch
        {
            DemoOutputType.Msi => "*.msi",
            DemoOutputType.Bundle => "*.exe",
            DemoOutputType.MergeModule => "*.msm",
            DemoOutputType.Patch => "*.msp",
            DemoOutputType.Transform => "*.mst",
            _ => throw new ArgumentOutOfRangeException(nameof(outputType)),
        };

        return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private void EnsureBundleDependencies(DemoExpectation bundleDemo)
    {
        // Build all MSI demos that share the same parent directory as the bundle.
        // E.g., for "06-product-suite/suite-bundle", build "06-product-suite/app-installer" and "06-product-suite/service-installer".
        var parentPrefix = bundleDemo.Name.Contains('/')
            ? bundleDemo.Name[..bundleDemo.Name.IndexOf('/')]
            : bundleDemo.Name;

        foreach (var msiDemo in DemoTestCatalog.MsiDemos
            .Where(d => d.Name.StartsWith(parentPrefix + "/", StringComparison.Ordinal)))
        {
            // Build MSI into its own project directory so relative paths resolve
            var msiProjectDir = Path.GetDirectoryName(msiDemo.ProjectPath)!;
            var result = RunProcess(msiDemo, msiProjectDir);
            _cache.TryAdd(msiDemo.Name, result);

            // Bundle demos reference sibling MSIs by conventional name (e.g., "../app-installer/app-installer.msi").
            // The compiler names the output after the package (e.g., "Acme_Application-2.0.0.msi").
            // Create a copy with the conventional name so the bundle can find it.
            if (result.OutputFile is not null && File.Exists(result.OutputFile))
            {
                var dirName = Path.GetFileName(msiProjectDir);
                var conventionalPath = Path.Combine(msiProjectDir, $"{dirName}.msi");
                if (!File.Exists(conventionalPath))
                {
                    File.Copy(result.OutputFile, conventionalPath);
                    _filesToCleanup.Add(conventionalPath);
                }

                // Also track the compiler-named MSI for cleanup
                _filesToCleanup.Add(result.OutputFile);
            }
        }
    }

    public void Dispose()
    {
        // Clean up files written into demo project directories (e.g., MSI copies for bundle deps)
        foreach (var file in _filesToCleanup)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* best effort */ }
        }

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup -- temp directory will be cleaned by OS eventually.
        }
    }
}
