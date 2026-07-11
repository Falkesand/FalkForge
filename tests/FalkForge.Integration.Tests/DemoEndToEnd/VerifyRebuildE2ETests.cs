using System.Diagnostics;
using System.Runtime.Versioning;
using FalkForge.Cli;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

/// <summary>
/// End-to-end proof of <c>forge verify --rebuild</c> against demo 01 (hello-world), the one demo
/// that opts into <c>Reproducible()</c>. This drives the real pipeline — real <c>dotnet run</c>
/// rebuild via <see cref="VerifyCommand"/>'s production runner and a real byte comparison — to
/// show that:
/// <list type="bullet">
///   <item>building demo 01 reproducibly and verifying it against its own project yields
///   <c>VERIFIED</c> (exit 0);</item>
///   <item>flipping a single byte of the shipped artifact yields <c>MISMATCH</c> (exit 1).</item>
/// </list>
/// This is the flagship provability ceremony: an artifact can be independently rebuilt and
/// proven byte-for-byte to come from its source.
/// </summary>
[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
[Trait("Category", "E2E")]
public sealed class VerifyRebuildE2ETests : IDisposable
{
    // Pinned epoch matching DemoBuildFixture so the reproducible build is deterministic.
    private const long Epoch = 1577836800;
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);

    private static readonly string Demo01Project = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "demo", "01-hello-world", "01-hello-world.csproj"));

    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(), $"falk-verify-e2e-{Guid.NewGuid():N}");

    public VerifyRebuildE2ETests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true); }
        catch { /* best effort */ }
    }

    private static CommandContext Ctx() =>
        new([], new NoRemainingArgs(), "verify", null);

    [Fact]
    public void Verify_Demo01ReproducibleRebuild_IsVerified()
    {
        E2EGate.SkipUnlessOptedIn();

        var shippedMsi = BuildDemo01("shipped");

        var output = new CapturingOutput();
        var command = new VerifyCommand(output);
        var settings = new VerifySettings
        {
            ArtifactPath = shippedMsi,
            RebuildProjectPath = Demo01Project,
            SourceDateEpoch = Epoch,
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.True(code == ExitCodes.Success,
            $"Expected VERIFIED (0) but got {code}.\nOutput:\n{string.Join("\n", output.All)}");
        Assert.Contains(output.All, m => m.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Verify_TamperedDemo01Artifact_IsMismatch()
    {
        E2EGate.SkipUnlessOptedIn();

        var shippedMsi = BuildDemo01("tampered");

        // Flip one byte deep inside the cabinet/stream region so the rebuild no longer matches.
        var bytes = File.ReadAllBytes(shippedMsi);
        var idx = bytes.Length / 2;
        bytes[idx] = (byte)(bytes[idx] ^ 0xFF);
        File.WriteAllBytes(shippedMsi, bytes);

        var output = new CapturingOutput();
        var command = new VerifyCommand(output);
        var settings = new VerifySettings
        {
            ArtifactPath = shippedMsi,
            RebuildProjectPath = Demo01Project,
            SourceDateEpoch = Epoch,
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.True(code == ExitCodes.ValidationFailure,
            $"Expected MISMATCH (1) but got {code}.\nOutput:\n{string.Join("\n", output.All)}");
        Assert.Contains(output.All, m => m.Contains("MISMATCH", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds demo 01 reproducibly into a subdirectory of the scratch root and returns the MSI path.
    /// Uses the same <c>dotnet run -- -o</c> invocation as the demo fixture so the "shipped" artifact
    /// is produced by the real build, independent of the verifier's own rebuild.
    /// </summary>
    private string BuildDemo01(string label)
    {
        var outDir = Path.Combine(_scratch, label);
        Directory.CreateDirectory(outDir);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(Demo01Project)!,
            },
        };
        foreach (var arg in new[] { "run", "--project", Demo01Project, "--", "-o", outDir })
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["SOURCE_DATE_EPOCH"] = Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture);
        process.StartInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)BuildTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new Xunit.Sdk.XunitException("Demo 01 shipped build timed out.");
        }
        stdout.Wait();
        stderr.Wait();

        var msi = Directory.EnumerateFiles(outDir, "*.msi", SearchOption.AllDirectories).FirstOrDefault();
        Assert.True(msi is not null,
            $"Demo 01 shipped build produced no MSI (exit {process.ExitCode}).\nStdout:\n{stdout.Result}\nStderr:\n{stderr.Result}");
        return msi!;
    }
}

file sealed class CapturingOutput : IConsoleOutput
{
    private readonly List<string> _all = [];
    public IReadOnlyList<string> All => _all;
    public void MarkupLine(string markup) => _all.Add(markup);
    public void WriteLine(string text) => _all.Add(text);
    public void WriteError(string text) => _all.Add(text);
}

file sealed class NoRemainingArgs : IRemainingArguments
{
    public IReadOnlyList<string> Raw => [];
    public ILookup<string, string?> Parsed => Array.Empty<string>().ToLookup(x => x, x => (string?)null);
}
