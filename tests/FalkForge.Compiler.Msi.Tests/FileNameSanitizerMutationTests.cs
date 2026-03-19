using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class FileNameSanitizerMutationTests
{
    [Fact]
    public void Sanitize_NormalFileName_ReturnsUnchanged()
    {
        var result = FileNameSanitizer.Sanitize("myfile.txt");

        Assert.Equal("myfile.txt", result);
    }

    [Fact]
    public void Sanitize_SpacesReplacedWithUnderscores()
    {
        var result = FileNameSanitizer.Sanitize("my file name.txt");

        Assert.Equal("my_file_name.txt", result);
        Assert.DoesNotContain(" ", result);
    }

    [Fact]
    public void Sanitize_BackslashReplacedWithUnderscore()
    {
        var result = FileNameSanitizer.Sanitize(@"sub\file.txt");

        Assert.Equal("sub_file.txt", result);
        Assert.DoesNotContain(@"\", result);
    }

    [Fact]
    public void Sanitize_ForwardSlashReplacedWithUnderscore()
    {
        var result = FileNameSanitizer.Sanitize("sub/file.txt");

        Assert.Equal("sub_file.txt", result);
        Assert.DoesNotContain("/", result);
    }

    [Fact]
    public void Sanitize_ColonReplacedWithUnderscore()
    {
        var result = FileNameSanitizer.Sanitize("file:name.txt");

        Assert.Equal("file_name.txt", result);
        Assert.DoesNotContain(":", result);
    }

    [Fact]
    public void Sanitize_PreservesLettersDigitsDotsDashesUnderscores()
    {
        var result = FileNameSanitizer.Sanitize("my_file-v2.0.dll");

        Assert.Equal("my_file-v2.0.dll", result);
    }

    [Fact]
    public void Sanitize_PathTraversal_BackslashesReplacedDotsPreserved()
    {
        var result = FileNameSanitizer.Sanitize(@"..\..\secret");

        // Backslashes replaced with _, dots preserved, spaces replaced with _
        Assert.DoesNotContain(@"\", result);
        Assert.Contains("..", result); // dots are valid in filenames
        Assert.Contains("secret", result);
    }

    [Fact]
    public void Sanitize_OutputLength_SameAsInput()
    {
        var input = "test file.txt";
        var result = FileNameSanitizer.Sanitize(input);

        Assert.Equal(input.Length, result.Length);
    }

    [Fact]
    public void Sanitize_MultipleConsecutiveInvalidChars_EachReplacedIndividually()
    {
        var result = FileNameSanitizer.Sanitize("a<>b");

        // Each invalid char should be replaced with underscore individually
        Assert.Equal("a__b", result);
        Assert.Equal(4, result.Length);
    }

    [Fact]
    public void Sanitize_ContainsNoPathSeparatorChars()
    {
        var input = @"a/b\c:d*e?f""g<h>i|j";
        var result = FileNameSanitizer.Sanitize(input);

        // Check visible invalid chars (skip null char which ToString returns empty)
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
    public void Sanitize_EmptyString_ReturnsEmpty()
    {
        var result = FileNameSanitizer.Sanitize("");

        Assert.Equal("", result);
    }

    [Fact]
    public void Sanitize_DotsPreserved()
    {
        var result = FileNameSanitizer.Sanitize("file.with.dots.exe");

        Assert.Equal("file.with.dots.exe", result);
    }

    [Fact]
    public void Sanitize_DashPreserved()
    {
        var result = FileNameSanitizer.Sanitize("my-app-v2.exe");

        Assert.Equal("my-app-v2.exe", result);
    }
}
