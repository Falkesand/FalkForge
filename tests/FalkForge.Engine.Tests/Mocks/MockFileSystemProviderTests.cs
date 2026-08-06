namespace FalkForge.Engine.Tests.Mocks;

using Xunit;

/// <summary>
/// Verifies <see cref="MockFileSystemProvider.GetDirectories"/> derives children from Windows-style
/// backslash paths deterministically. Production code (<c>WindowsFileSystemProvider</c>) only ever
/// runs on Windows, so every path this mock is seeded with throughout the suite is Windows-style
/// (e.g. <c>@"C:\dotnet\shared\Foo\10.0.10"</c>). <see cref="Path.GetDirectoryName(string)"/> splits
/// on the *host runtime's* separator set, which is backslash-aware on Windows but not on other
/// runtimes -- so deriving a parent with it would silently stop matching on a non-Windows test run
/// even though the seeded path shape never changes. <see cref="MockFileSystemProvider"/> must not
/// depend on that OS-conditional behavior for a path shape that is always Windows-style regardless of
/// which OS is running the test.
/// </summary>
public sealed class MockFileSystemProviderTests
{
    [Fact]
    public void GetDirectories_WithBackslashSeparatedPath_ReturnsRegisteredChild()
    {
        // Arrange -- a nested Windows-style path, exactly the shape production code seeds this mock
        // with (see SearchConditionEvaluatorTests' shared-framework scenarios).
        const string child = @"C:\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.10";
        var fs = new MockFileSystemProvider().WithDirectory(child);

        // Act
        var children = fs.GetDirectories(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App");

        // Assert -- parent derivation must match on the literal backslash, not on whatever the host
        // runtime's Path.DirectorySeparatorChar happens to be.
        Assert.Equal([child], children);
    }

    [Fact]
    public void GetDirectories_WithMultipleSiblings_ReturnsOnlyDirectChildrenOfRequestedPath()
    {
        // Arrange -- siblings at the requested level plus one at a shallower level that must not
        // be picked up (GetDirectories is one level, not a recursive descendant search).
        const string basePath = @"C:\dotnet\shared\Microsoft.WindowsDesktop.App";
        var fs = new MockFileSystemProvider()
            .WithDirectory(basePath + @"\10.0.10")
            .WithDirectory(basePath + @"\9.0.17")
            .WithDirectory(@"C:\dotnet\shared");

        // Act
        var children = fs.GetDirectories(basePath);

        // Assert -- ordinal string ordering ("10.0.10" sorts before "9.0.17" since '1' < '9'), not
        // version-numeric ordering; GetDirectories itself makes no ordering promise.
        Assert.Equal(
            [basePath + @"\10.0.10", basePath + @"\9.0.17"],
            children.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetDirectories_WithNestedDescendant_ExcludesEntriesDeeperThanOneLevel()
    {
        // Arrange -- a direct child plus a descendant nested two levels below the requested path.
        // GetDirectories derives a parent by exact match on GetParentPath(d), not by prefix, so a
        // deeper descendant must not leak into a one-level enumeration even though its full path
        // starts with the requested path. If that derivation ever regressed to a prefix match, both
        // entries below would satisfy it and this test would catch it while the multi-sibling test
        // above (which only guards against a *shallower* entry leaking in) would stay green.
        const string basePath = @"C:\dotnet\shared\Microsoft.WindowsDesktop.App";
        const string directChild = basePath + @"\10.0.10";
        const string nestedDescendant = directChild + @"\some-nested-dir";
        var fs = new MockFileSystemProvider()
            .WithDirectory(directChild)
            .WithDirectory(nestedDescendant);

        // Act
        var children = fs.GetDirectories(basePath);

        // Assert -- only the direct child comes back; the nested descendant two levels down is
        // excluded even though its path is prefixed by basePath.
        Assert.Equal([directChild], children);
    }

    [Fact]
    public void GetDirectories_WithNoMatchingChildren_ReturnsEmpty()
    {
        // Arrange
        var fs = new MockFileSystemProvider().WithDirectory(@"C:\unrelated\path");

        // Act
        var children = fs.GetDirectories(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App");

        // Assert
        Assert.Empty(children);
    }
}
