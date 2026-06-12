using System.Text;
using FalkForge.Cli;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Verifies <see cref="PlanCommand"/> does not leak its per-run scratch directory
/// (<c>%TEMP%\FalkForge\plan_&lt;guid&gt;</c>). Intent: repeated <c>forge plan</c> invocations
/// must not accumulate orphaned temp directories on disk, so the command must clean up its
/// scratch dir in a finally block regardless of how the run ends.
/// </summary>
public sealed class PlanCommandTempCleanupTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "plan", null);

    private static string FalkForgeTempRoot =>
        Path.Combine(Path.GetTempPath(), "FalkForge");

    private static int CountPlanTempDirs()
    {
        if (!Directory.Exists(FalkForgeTempRoot))
            return 0;
        return Directory.GetDirectories(FalkForgeTempRoot, "plan_*").Length;
    }

    [Fact]
    public void Execute_DoesNotLeavePlanScratchDirectoryBehind()
    {
        // Build a real bundle EXE so the command gets past manifest extraction and reaches the
        // temp-dir creation + engine-launch path, then exits (the fake launcher reports failure
        // because it writes no plan file). The scratch dir must still be cleaned up.
        var bundlePath = Path.Combine(Path.GetTempPath(), $"FalkPlanClean_{Guid.NewGuid():N}.exe");
        CreateMinimalBundle(bundlePath);
        try
        {
            var before = CountPlanTempDirs();

            var launcher = new CleanupFakeLauncher();
            var output = new TestConsoleOutput();
            var command = new PlanCommand(output, launcher: launcher);
            var settings = new PlanSettings { ProjectPath = bundlePath };

            command.Execute(CreateContext(), settings, CancellationToken.None);

            var after = CountPlanTempDirs();
            Assert.Equal(before, after);
        }
        finally
        {
            if (File.Exists(bundlePath))
                File.Delete(bundlePath);
        }
    }

    // Writes a minimal FALKBUNDLE with an embedded "{}" manifest and no payloads, matching
    // PayloadEmbedder's format: [stub][magic][manifestLen:int32][manifest][TOC][footer magic][tocOffset:int64].
    // Enough for BundleReader.Extract to return a non-empty manifest so PlanCommand reaches its
    // temp-dir creation + engine-launch path (the engine binary is absent in the test host, so the
    // command exits afterward — exactly the path whose cleanup we are asserting).
    //
    // SYNC NOTE: This hand-built binary layout must stay in sync with BundleContent/BundleReader
    // (PayloadEmbedder writes it, BundleReader.Extract reads it). If the format changes,
    // BundleReader.Extract will fail at extraction with a format error — not silently pass — so
    // a drift will surface as a hard test failure here, not a silent green.
    private static void CreateMinimalBundle(string path)
    {
        var magic = Encoding.ASCII.GetBytes("FALKBUNDLE\0\0\0\0\0\0");
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(new byte[] { 0x4D, 0x5A }); // tiny PE-ish stub
        writer.Write(magic);

        var manifestBytes = "{}"u8.ToArray();
        writer.Write(manifestBytes.Length);
        writer.Write(manifestBytes);

        var tocOffset = stream.Position;
        writer.Write(0); // zero TOC entries

        writer.Write(magic);
        writer.Write(tocOffset);
    }
}

file sealed class CleanupFakeLauncher : IEngineLauncher
{
    public Task<EngineLaunchResult> LaunchAsync(string exePath, string[] args, CancellationToken ct)
        => Task.FromResult(new EngineLaunchResult(0, "{}"));
}
