using Xunit;

namespace FalkForge.Core.Tests;

public sealed class InstallPathTests
{
    [Fact]
    public void Segments_SingleSegment_ReturnsOne()
    {
        var path = KnownFolder.ProgramFiles / "MyApp";

        Assert.Single(path.Segments);
        Assert.Equal("MyApp", path.Segments[0]);
    }

    [Fact]
    public void SlashOperator_AppendsSubpath()
    {
        var basePath = KnownFolder.ProgramFiles / "Contoso";
        var extended = basePath / "bin";

        Assert.Equal("Contoso/bin", extended.RelativePath);
        Assert.Same(basePath.Root, extended.Root);
    }

    [Fact]
    public void ToString_IncludesTokenAndRelativePath()
    {
        var path = KnownFolder.ProgramFiles / "Contoso" / "MyApp";

        Assert.Equal("[ProgramFilesFolder]Contoso/MyApp", path.ToString());
    }

    [Fact]
    public void Backslashes_AreNormalizedToForwardSlashes()
    {
        var path = KnownFolder.ProgramFiles / "Contoso\\SubDir\\MyApp";

        Assert.Equal("Contoso/SubDir/MyApp", path.RelativePath);
        Assert.DoesNotContain("\\", path.RelativePath);
    }

    [Fact]
    public void TrailingSlash_IsTrimmed()
    {
        var path = KnownFolder.ProgramFiles / "MyApp/";

        Assert.Equal("MyApp", path.RelativePath);
    }

    [Fact]
    public void Equality_SameRootAndPath_AreEqual()
    {
        var path1 = KnownFolder.ProgramFiles / "Contoso" / "MyApp";
        var path2 = KnownFolder.ProgramFiles / "Contoso" / "MyApp";

        Assert.Equal(path1, path2);
        Assert.True(path1.Equals(path2));
    }

    [Fact]
    public void Equality_DifferentRoot_AreNotEqual()
    {
        var path1 = KnownFolder.ProgramFiles / "MyApp";
        var path2 = KnownFolder.ProgramFiles64 / "MyApp";

        Assert.NotEqual(path1, path2);
    }

    [Fact]
    public void Equality_DifferentPath_AreNotEqual()
    {
        var path1 = KnownFolder.ProgramFiles / "AppA";
        var path2 = KnownFolder.ProgramFiles / "AppB";

        Assert.NotEqual(path1, path2);
    }

    [Fact]
    public void GetHashCode_EqualPaths_HaveSameHashCode()
    {
        var path1 = KnownFolder.ProgramFiles / "Contoso" / "MyApp";
        var path2 = KnownFolder.ProgramFiles / "Contoso" / "MyApp";

        Assert.Equal(path1.GetHashCode(), path2.GetHashCode());
    }

    [Fact]
    public void SlashOperator_Chained_BuildsDeepPath()
    {
        var path = KnownFolder.ProgramFiles / "A" / "B" / "C" / "D";

        Assert.Equal("A/B/C/D", path.RelativePath);
        Assert.Equal(4, path.Segments.Count);
    }

    [Fact]
    public void SlashOperator_WithBackslashSubpath_Normalizes()
    {
        var basePath = KnownFolder.ProgramFiles / "Contoso";
        var extended = basePath / "sub\\dir";

        Assert.Equal("Contoso/sub/dir", extended.RelativePath);
    }

    [Fact]
    public void Segments_CalledTwice_ReturnsSameCachedInstance()
    {
        var path = KnownFolder.ProgramFiles / "Contoso" / "MyApp";

        var first = path.Segments;
        var second = path.Segments;

        Assert.Equal(new[] { "Contoso", "MyApp" }, first);
        // Reference-equal proves the second call reused the cached split
        // instead of re-splitting RelativePath.
        Assert.Same(first, second);
    }

    [Fact]
    public void ToString_CalledTwice_ReturnsSameCachedInstance()
    {
        var path = KnownFolder.ProgramFiles / "Contoso" / "MyApp";

        var first = path.ToString();
        var second = path.ToString();

        Assert.Equal("[ProgramFilesFolder]Contoso/MyApp", first);
        Assert.Same(first, second);
    }
}
