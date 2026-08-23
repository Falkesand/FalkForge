namespace FalkForge.Platform.Windows.Tests;

using FalkForge.Platform.Windows;
using Xunit;

/// <summary>
/// <see cref="MsiTransformArgs.MergeTransforms"/> must produce exactly one <c>TRANSFORMS</c> property:
/// appended after an author-set value with a <c>;</c>, or added fresh when none is present. A second
/// <c>TRANSFORMS=</c> pair would make Windows Installer silently drop one.
/// </summary>
public sealed class MsiTransformArgsTests
{
    [Fact]
    public void MergeTransforms_NoExisting_AppendsNewPair()
    {
        var merged = MsiTransformArgs.MergeTransforms(
            " INSTALLDIR=\"C:\\App\"", @"C:\stage\secret.mst");

        Assert.Equal(" INSTALLDIR=\"C:\\App\" TRANSFORMS=\"C:\\stage\\secret.mst\"", merged);
    }

    [Fact]
    public void MergeTransforms_EmptyArgs_AddsPair()
    {
        var merged = MsiTransformArgs.MergeTransforms(string.Empty, @"C:\stage\secret.mst");
        Assert.Equal(" TRANSFORMS=\"C:\\stage\\secret.mst\"", merged);
    }

    [Fact]
    public void MergeTransforms_ExistingAuthorTransforms_CombinesWithSemicolon()
    {
        // An author set TRANSFORMS via SetProperty; the secret transform must join it, not replace it and
        // not add a second pair.
        var merged = MsiTransformArgs.MergeTransforms(
            " TRANSFORMS=\"C:\\author\\lang.mst\"", @"C:\stage\secret.mst");

        Assert.Equal(" TRANSFORMS=\"C:\\author\\lang.mst;C:\\stage\\secret.mst\"", merged);
        // Exactly one TRANSFORMS pair.
        Assert.Equal(1, CountOccurrences(merged, "TRANSFORMS=\""));
    }

    [Fact]
    public void MergeTransforms_ExistingTransformsAmongOtherPairs_MergesOnlyThatValue()
    {
        var merged = MsiTransformArgs.MergeTransforms(
            " INSTALLDIR=\"C:\\App\" TRANSFORMS=\"a.mst\" ADDLOCAL=\"F1\"", "b.mst");

        Assert.Equal(
            " INSTALLDIR=\"C:\\App\" TRANSFORMS=\"a.mst;b.mst\" ADDLOCAL=\"F1\"", merged);
    }

    [Fact]
    public void MergeTransforms_TransformsTextInsideAnotherValue_IsNotMistakenForThePair()
    {
        // A property whose value merely contains the text TRANSFORMS= must not be treated as the pair.
        var merged = MsiTransformArgs.MergeTransforms(
            " NOTE=\"see TRANSFORMS=later\"", "b.mst");

        Assert.Equal(" NOTE=\"see TRANSFORMS=later\" TRANSFORMS=\"b.mst\"", merged);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
