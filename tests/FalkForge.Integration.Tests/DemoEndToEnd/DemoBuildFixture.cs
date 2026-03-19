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

    public DemoBuildFixture()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public DemoBuildResult GetOrBuild(DemoExpectation demo) =>
        _cache.GetOrAdd(demo.Name, _ => Build(demo));

    private DemoBuildResult Build(DemoExpectation demo)
    {
        // Bundle demos reference sibling MSI projects via relative paths (e.g., ../app-installer/app-installer.msi).
        // For bundles, we must first ensure their MSI dependencies are built into the expected locations.
        if (demo.OutputType == DemoOutputType.Bundle)
            EnsureBundleDependencies(demo);

        var outputDir = Path.Combine(_tempRoot, demo.Name.Replace('/', '_'));
        Directory.CreateDirectory(outputDir);
        return RunProcess(demo, outputDir);
    }

    private static DemoBuildResult RunProcess(DemoExpectation demo, string outputDir)
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
