using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Tables;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class DirectoryTreeSynthesizerTests
{
    [Fact]
    public void ComputeDirectoryId_RootOnly_ReturnsRootToken()
    {
        InstallPath path = KnownFolder.ProgramFiles / string.Empty;

        string id = DirectoryTreeSynthesizer.ComputeDirectoryId(path, installDir: null);

        Assert.Equal(KnownFolder.ProgramFiles.Token, id);
    }

    [Fact]
    public void ComputeDirectoryId_InstallDirLeaf_ReturnsInstallDirToken()
    {
        InstallPath installDir = KnownFolder.ProgramFiles / "Contoso" / "App";

        string id = DirectoryTreeSynthesizer.ComputeDirectoryId(installDir, installDir);

        Assert.Equal(WellKnownDirectoryIds.InstallDir, id);
    }

    [Fact]
    public void ComputeDirectoryId_CleanSegments_ProducesGeneratedIdWithPrefix()
    {
        InstallPath path = KnownFolder.ProgramFiles / "Contoso" / "App";

        string id = DirectoryTreeSynthesizer.ComputeDirectoryId(path, installDir: null);

        Assert.StartsWith("D_", id);
        Assert.Contains("App", id);
    }

    [Fact]
    public void ComputeDirectoryId_SegmentWithSpecialCharacters_SanitizesToUnderscore()
    {
        InstallPath path = KnownFolder.ProgramFiles / "My App (v2)";

        string id = DirectoryTreeSynthesizer.ComputeDirectoryId(path, installDir: null);

        // Spaces and parentheses are not letters/digits/'_'/'.', so the sanitizer
        // replaces each with '_'. This exercises the "needs replacement" branch of
        // SanitizeId, distinct from the all-clean fast path covered above.
        Assert.StartsWith("D_My_App__v2__", id);
    }

    [Fact]
    public void ComputeDirectoryId_SameInputTwice_IsDeterministic()
    {
        InstallPath path = KnownFolder.ProgramFiles / "Contoso" / "App" / "bin";

        string id1 = DirectoryTreeSynthesizer.ComputeDirectoryId(path, installDir: null);
        string id2 = DirectoryTreeSynthesizer.ComputeDirectoryId(path, installDir: null);

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void ComputeDirectoryId_DifferentParents_ProduceDifferentIdsForSameLeafName()
    {
        InstallPath pathA = KnownFolder.ProgramFiles / "AppA" / "bin";
        InstallPath pathB = KnownFolder.ProgramFiles / "AppB" / "bin";

        string idA = DirectoryTreeSynthesizer.ComputeDirectoryId(pathA, installDir: null);
        string idB = DirectoryTreeSynthesizer.ComputeDirectoryId(pathB, installDir: null);

        Assert.NotEqual(idA, idB);
    }

    [Fact]
    public void BuildPrefixPath_ReturnsPrefixOfRequestedLength()
    {
        InstallPath path = KnownFolder.ProgramFiles / "A" / "B" / "C";

        InstallPath prefix = DirectoryTreeSynthesizer.BuildPrefixPath(path, 2);

        Assert.Equal("A/B", prefix.RelativePath);
    }

    [Fact]
    public void BuildPrefixPath_FullLength_ReturnsSamePathReference()
    {
        InstallPath path = KnownFolder.ProgramFiles / "A" / "B";

        InstallPath prefix = DirectoryTreeSynthesizer.BuildPrefixPath(path, path.Segments.Count);

        Assert.Same(path, prefix);
    }

    [Fact]
    public void ComputeDirectoryId_ViaBuildPrefixPath_MatchesWalkingEachSegment()
    {
        // Mirrors how DirectoryTableProducer.EnsureInstallPathRows walks a path:
        // computing the id at each prefix depth. Verifies the two synthesizer
        // entry points agree with each other for a shared multi-segment path.
        InstallPath path = KnownFolder.ProgramFiles / "Contoso" / "App" / "plugins";

        string idAtDepth2 = DirectoryTreeSynthesizer.ComputeDirectoryId(
            DirectoryTreeSynthesizer.BuildPrefixPath(path, 2), installDir: null);
        string idAtDepth2Direct = DirectoryTreeSynthesizer.ComputeDirectoryId(
            KnownFolder.ProgramFiles / "Contoso" / "App", installDir: null);

        Assert.Equal(idAtDepth2Direct, idAtDepth2);
    }
}
