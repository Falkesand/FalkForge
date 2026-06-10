using Xunit;

namespace FalkForge.Tests;

/// <summary>
/// Tests for the shared <see cref="FileNameSanitizer"/> utility in Core.
/// Covers the union of behaviors from all former call sites:
///   - FalkForge.Compiler.Msi (invalid chars → '_', space → '_')
///   - FalkForge.Compiler.Msix (same, plus caller trims result)
///   - FalkForge.Cli.Commands.BuildCommand (same as Msi)
///   - FalkForge.Studio.Export.CiCdExporter (invalid chars → '-', space → '-')
/// </summary>
public sealed class FileNameSanitizerTests
{
    // ── Default replacement ('_') ─────────────────────────────────────────────

    [Theory]
    [InlineData(@"..\evil", ".._evil")]
    [InlineData(@"..\..\evil", ".._.._evil")]
    [InlineData("normal", "normal")]
    [InlineData("has space", "has_space")]
    [InlineData("file/path", "file_path")]
    [InlineData("file:name", "file_name")]
    [InlineData("ok-name", "ok-name")]
    public void Sanitize_DefaultReplacement_InvalidCharsAndSpaceReplacedWithUnderscore(
        string input, string expected)
    {
        var result = FileNameSanitizer.Sanitize(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Sanitize_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", FileNameSanitizer.Sanitize(""));
    }

    [Fact]
    public void Sanitize_OutputLength_SameAsInput()
    {
        var result = FileNameSanitizer.Sanitize("test file.txt");

        Assert.Equal("test file.txt".Length, result.Length);
    }

    [Fact]
    public void Sanitize_MultipleConsecutiveInvalidChars_EachReplacedIndividually()
    {
        var result = FileNameSanitizer.Sanitize("a<>b");

        Assert.Equal("a__b", result);
        Assert.Equal(4, result.Length);
    }

    [Fact]
    public void Sanitize_AllKnownInvalidChars_NoneRemainInResult()
    {
        var input = @"a/b\c:d*e?f""g<h>i|j";
        var result = FileNameSanitizer.Sanitize(input);

        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain(@"\", result);
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain("|", result);
        Assert.DoesNotContain(" ", result);
    }

    [Fact]
    public void Sanitize_SingleCharFileName_Works()
    {
        Assert.Equal("a", FileNameSanitizer.Sanitize("a"));
        Assert.Equal("_", FileNameSanitizer.Sanitize(" "));
        Assert.Equal("_", FileNameSanitizer.Sanitize("/"));
    }

    [Fact]
    public void Sanitize_DotsPreserved()
    {
        Assert.Equal("file.with.dots.exe", FileNameSanitizer.Sanitize("file.with.dots.exe"));
    }

    [Fact]
    public void Sanitize_DashPreserved()
    {
        Assert.Equal("my-app-v2.exe", FileNameSanitizer.Sanitize("my-app-v2.exe"));
    }

    [Fact]
    public void Sanitize_ResultContainsNoPathSeparators()
    {
        var result = FileNameSanitizer.Sanitize(@"..\..\Windows\System32\evil");

        Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), result);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar.ToString(), result);
    }

    [Fact]
    public void Sanitize_OutputCombinedWithDirectory_StaysWithinOutputDir()
    {
        var outputDir = Path.GetTempPath();
        var sanitized = FileNameSanitizer.Sanitize(@"..\..\evil");
        var fullPath = Path.GetFullPath(Path.Combine(outputDir, $"{sanitized}.mst"));

        Assert.StartsWith(outputDir, fullPath);
    }

    // ── Custom replacement ('-') — covers CiCdExporter behavior ──────────────

    [Theory]
    [InlineData("Simple App", '-', "Simple-App")]
    [InlineData("App<>Name", '-', "App--Name")]
    [InlineData("My:App|Test", '-', "My-App-Test")]
    [InlineData("Normal", '-', "Normal")]
    [InlineData("Has/Slash", '-', "Has-Slash")]
    [InlineData(@"Has\Backslash", '-', "Has-Backslash")]
    public void Sanitize_DashReplacement_InvalidCharsAndSpaceReplacedWithDash(
        string input, char replacement, string expected)
    {
        var result = FileNameSanitizer.Sanitize(input, replacement);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Sanitize_DashReplacement_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", FileNameSanitizer.Sanitize("", '-'));
    }

    [Fact]
    public void Sanitize_DashReplacement_ResultContainsNoInvalidChars()
    {
        var result = FileNameSanitizer.Sanitize("my app: v1.0/test", '-');

        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain(" ", result);
        Assert.Contains("-", result);
    }

    // ── MSIX-specific: space not treated as invalid by Path.GetInvalidFileNameChars ──

    [Fact]
    public void Sanitize_SpaceIsAlwaysReplaced_RegardlessOfGetInvalidFileNameCharsContents()
    {
        // Space is NOT in Path.GetInvalidFileNameChars() on all platforms,
        // but all call sites require it replaced. Verify the implementation handles it.
        var result = FileNameSanitizer.Sanitize("Simple App");

        Assert.DoesNotContain(" ", result);
        Assert.Equal("Simple_App", result);
    }
}
