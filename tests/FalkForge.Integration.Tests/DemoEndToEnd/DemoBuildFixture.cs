using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[SupportedOSPlatform("windows")]
public sealed class DemoBuildFixture : IDisposable
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, DemoBuildResult> _cache = new();
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
        var outputDir = Path.Combine(_tempRoot, demo.Name.Replace('/', '_'));
        Directory.CreateDirectory(outputDir);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{demo.ProjectPath}\" -- -o \"{outputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

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

        return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }

    public void Dispose()
    {
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
