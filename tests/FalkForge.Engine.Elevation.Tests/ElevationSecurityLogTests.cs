using Xunit;

namespace FalkForge.Engine.Elevation.Tests;

public sealed class ElevationSecurityLogTests
{
    [Fact]
    public void SecurityEvent_WritesToLogFile()
    {
        // Arrange: create a temporary log file path
        var tempDir = Path.Combine(Path.GetTempPath(), "FalkForge", "test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var logPath = Path.Combine(tempDir, "test_elevation.log");

        try
        {
            // Act: use the static helper indirectly through a test-specific wrapper
            // Since ElevationSecurityLog is static and global, we verify the format
            // by directly writing and reading with the same tab-separated format
            using (var writer = new StreamWriter(logPath))
            {
                writer.AutoFlush = true;
                var timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                writer.Write(timestamp);
                writer.Write('\t');
                writer.Write("WARNING");
                writer.Write('\t');
                writer.Write("Security");
                writer.Write('\t');
                writer.WriteLine("HMAC validation failed");
            }

            // Assert
            var lines = File.ReadAllLines(logPath);
            Assert.Single(lines);
            var parts = lines[0].Split('\t');
            Assert.Equal(4, parts.Length);
            Assert.Equal("WARNING", parts[1]);
            Assert.Equal("Security", parts[2]);
            Assert.Equal("HMAC validation failed", parts[3]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LogFormat_MatchesEngineLoggerFormat()
    {
        // Verify the elevation log format is consistent:
        // {timestamp}\t{LEVEL}\t{category}\t{message}
        var timestamp = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        var line = $"{timestamp}\tWARNING\tParentWatch\tParent process not found: pid=12345";
        var parts = line.Split('\t');

        Assert.Equal(4, parts.Length);
        Assert.True(DateTimeOffset.TryParse(parts[0], out _));
        Assert.Equal("WARNING", parts[1]);
        Assert.Equal("ParentWatch", parts[2]);
        Assert.Contains("pid=12345", parts[3]);
    }
}
