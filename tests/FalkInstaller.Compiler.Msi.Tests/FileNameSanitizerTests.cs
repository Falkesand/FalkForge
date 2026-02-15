using Xunit;

namespace FalkInstaller.Compiler.Msi.Tests;

public sealed class FileNameSanitizerTests
{
    [Theory]
    [InlineData(@"..\evil", ".._evil")]
    [InlineData(@"..\..\evil", ".._.._evil")]
    [InlineData("normal", "normal")]
    [InlineData("has space", "has_space")]
    [InlineData("file/path", "file_path")]
    [InlineData("file:name", "file_name")]
    [InlineData("ok-name", "ok-name")]
    public void Sanitize_PathTraversalChars_AreReplaced(string input, string expected)
    {
        var result = FileNameSanitizer.Sanitize(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Sanitize_ResultContainsNoPathSeparators()
    {
        var malicious = @"..\..\Windows\System32\evil";

        var result = FileNameSanitizer.Sanitize(malicious);

        Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), result);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar.ToString(), result);
    }

    [Fact]
    public void Sanitize_OutputCombinedWithDirectory_StaysWithinOutputDir()
    {
        var outputDir = Path.GetTempPath();
        var maliciousId = @"..\..\evil";

        var sanitized = FileNameSanitizer.Sanitize(maliciousId);
        var fullPath = Path.GetFullPath(Path.Combine(outputDir, $"{sanitized}.mst"));

        Assert.StartsWith(outputDir, fullPath);
    }
}
