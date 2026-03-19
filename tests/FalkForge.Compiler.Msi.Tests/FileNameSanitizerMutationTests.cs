using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class FileNameSanitizerMutationTests
{
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
