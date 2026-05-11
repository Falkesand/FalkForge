using System.Reflection;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests;

/// <summary>
/// Tests that <see cref="ElevationSecurityLog"/> writes the session correlation id
/// as a fifth tab-separated column on every log line after
/// <see cref="ElevationSecurityLog.SetCorrelationId"/> is called.
/// </summary>
[Collection("ElevationSecurityLog")]
public sealed class ElevationSecurityLogCorrelationTests : IDisposable
{
    private static readonly FieldInfo WriterField =
        typeof(ElevationSecurityLog).GetField(
            "_writer",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("_writer field not found on ElevationSecurityLog");

    private static readonly FieldInfo InitializedField =
        typeof(ElevationSecurityLog).GetField(
            "_initialized",
            BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("_initialized field not found on ElevationSecurityLog");

    private readonly string _tempDir;
    private StreamWriter? _activeWriter;
    private string? _logFilePath;

    public ElevationSecurityLogCorrelationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ElevationCorrelationTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        ResetStaticState();
    }

    public void Dispose()
    {
        ResetStaticState();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best-effort */ }
    }

    private string InjectFreshWriter()
    {
        ElevationSecurityLog.Shutdown();

        _logFilePath = Path.Combine(_tempDir, $"test_{Guid.NewGuid():N}.log");
        var fileStream = new FileStream(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _activeWriter = new StreamWriter(fileStream, System.Text.Encoding.UTF8) { AutoFlush = true };

        WriterField.SetValue(null, _activeWriter);
        InitializedField.SetValue(null, true);

        return _logFilePath;
    }

    private void ResetStaticState()
    {
        try
        {
            var writer = WriterField.GetValue(null) as StreamWriter;
            writer?.Flush();
            writer?.Dispose();
        }
        catch { /* ignore */ }

        WriterField.SetValue(null, null);
        InitializedField.SetValue(null, false);

        // Also reset the correlation id field (it's a string, not a Guid)
        var correlationField = typeof(ElevationSecurityLog).GetField(
            "_correlationId",
            BindingFlags.Static | BindingFlags.NonPublic);
        correlationField?.SetValue(null, string.Empty);
    }

    private string[] ReadLogLines()
    {
        _activeWriter?.Flush();
        ElevationSecurityLog.Shutdown();
        _activeWriter = null;
        return File.ReadAllLines(_logFilePath!);
    }

    [Fact]
    public void SetCorrelationId_WrittenAsFifthColumn()
    {
        InjectFreshWriter();
        var correlationId = Guid.NewGuid();

        ElevationSecurityLog.SetCorrelationId(correlationId);
        ElevationSecurityLog.Info("Startup", "Elevation process started");

        var lines = ReadLogLines();

        Assert.Single(lines);
        var parts = lines[0].Split('\t');

        // Format: timestamp\tlevel\tcategory\tmessage\tcorrelationId
        Assert.Equal(5, parts.Length);
        Assert.Equal(correlationId.ToString("D"), parts[4]);
    }

    [Fact]
    public void WithoutSetCorrelationId_FifthColumnIsEmpty()
    {
        InjectFreshWriter();

        // Do NOT call SetCorrelationId — id stays Guid.Empty
        ElevationSecurityLog.Info("Startup", "No correlation");

        var lines = ReadLogLines();

        Assert.Single(lines);
        var parts = lines[0].Split('\t');

        // Fifth column should be empty when no id set
        Assert.Equal(5, parts.Length);
        Assert.Equal(string.Empty, parts[4]);
    }

    [Fact]
    public void SetCorrelationId_SameIdOnAllSubsequentLines()
    {
        InjectFreshWriter();
        var correlationId = Guid.NewGuid();

        ElevationSecurityLog.SetCorrelationId(correlationId);
        ElevationSecurityLog.Info("A", "first");
        ElevationSecurityLog.SecurityEvent("B", "second");
        ElevationSecurityLog.Error("C", "third");

        var lines = ReadLogLines();

        Assert.Equal(3, lines.Length);
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            Assert.Equal(5, parts.Length);
            Assert.Equal(correlationId.ToString("D"), parts[4]);
        }
    }

    [Fact]
    public void SetCorrelationId_AcceptsGuidEmpty_WritesEmptyColumn()
    {
        InjectFreshWriter();

        ElevationSecurityLog.SetCorrelationId(Guid.Empty);
        ElevationSecurityLog.Info("Test", "empty id");

        var lines = ReadLogLines();

        Assert.Single(lines);
        var parts = lines[0].Split('\t');
        Assert.Equal(5, parts.Length);
        Assert.Equal(string.Empty, parts[4]);
    }
}
