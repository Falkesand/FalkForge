using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Keeps the <c>FalkForge.Engine.Runtime.win-x64</c> package's two halves in step.
/// <para>
/// The csproj decides which executables the package CARRIES (each one packed to
/// <c>tools/engine</c>). The build props decide which of them a consuming project's build COPIES
/// into its own output under <c>engine\</c>, and that copied folder is where the bundle compiler
/// probes. A payload added to one half and not the other ships a package that looks complete and
/// breaks the consumer: the compiler cannot find the binary, falls through to a repo-only
/// resolution path a consumer does not have, and their build fails.
/// </para>
/// <para>
/// That is exactly what happened to <c>FalkForge.Ui.exe</c>: it was added to the package and left
/// out of the copy group. This test reads both files straight from the working tree, so the next
/// payload that drifts fails here rather than in someone else's build.
/// </para>
/// </summary>
public sealed class EngineRuntimePackageCopyGroupTests
{
    private const string PackedPayloadPath = "tools/engine";
    private const string PropsFile = "src/FalkForge.Engine.Runtime/build/FalkForge.Engine.Runtime.win-x64.props";
    private const string ProjectFile = "src/FalkForge.Engine.Runtime/FalkForge.Engine.Runtime.csproj";

    [Fact]
    public void EveryPackagedExecutable_IsAlsoCopiedIntoTheConsumersOutput()
    {
        var packaged = PackagedPayloadNames();
        var copied = CopiedPayloadNames();

        Assert.NotEmpty(packaged);
        Assert.Equal(packaged, copied);
    }

    /// <summary>
    /// A pack item written with a backslash separator or a trailing slash still points at
    /// <c>tools/engine</c> as far as MSBuild and NuGet are concerned. A strict Ordinal comparison
    /// against the literal constant would silently exclude such an item from the packed set, so the
    /// two halves of the guard would stay equal — and pass — even though the package now ships an
    /// executable the props file never learned to copy. This proves the matcher normalises before
    /// comparing rather than papering over a mismatch after the fact.
    /// </summary>
    [Theory]
    [InlineData("tools/engine", true)]
    [InlineData(@"tools\engine", true)]
    [InlineData("tools/engine/", true)]
    [InlineData(@"tools\engine\", true)]
    [InlineData("tools/other", false)]
    public void IsPackedPayload_NormalizesSeparatorsAndTrailingSlashBeforeComparing(string packagePath, bool expected)
    {
        var item = new XElement("None", new XAttribute("PackagePath", packagePath));

        Assert.Equal(expected, IsPackedPayload(item));
    }

    /// <summary>
    /// The copy group must land each payload under <c>engine\</c>, because that is the
    /// subdirectory the bundle compiler probes beside the host application. A payload copied to
    /// the output root instead would satisfy the set comparison above and still not be found.
    /// </summary>
    [Fact]
    public void EveryCopiedExecutable_LandsInTheEngineSubdirectory()
    {
        foreach (var item in CopyGroupItems())
        {
            var name = PayloadName(item);
            Assert.Equal($@"engine\{name}", Attribute(item, "Link"));
            Assert.Equal("PreserveNewest", Attribute(item, "CopyToOutputDirectory"));
        }
    }

    private static SortedSet<string> PackagedPayloadNames()
    {
        var project = XDocument.Load(RepoPath(ProjectFile));
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in project.Descendants("None"))
        {
            if (!IsPackedPayload(item))
                continue;

            names.Add(PayloadName(item));
        }

        return names;
    }

    /// <summary>
    /// Whether a pack item's <c>PackagePath</c> points at <see cref="PackedPayloadPath"/>. Separators
    /// and a trailing slash are normalised first: MSBuild accepts either separator and a consumer of
    /// the packed path would treat <c>tools/engine</c>, <c>tools\engine</c> and <c>tools/engine/</c>
    /// as the same folder, so a comparison that does not would silently drop a differently-spelled
    /// but still-correct pack item out of the packed set this test compares against the copy group.
    /// </summary>
    private static bool IsPackedPayload(XElement item)
        => string.Equals(NormalizePackagePath(Attribute(item, "PackagePath")), PackedPayloadPath, StringComparison.Ordinal);

    private static string NormalizePackagePath(string value)
        => value.Replace('\\', '/').TrimEnd('/');

    private static SortedSet<string> CopiedPayloadNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in CopyGroupItems())
            names.Add(PayloadName(item));

        return names;
    }

    /// <summary>
    /// The props file's copy group: the <c>None</c> items in the <c>ItemGroup</c> guarded by
    /// <c>FalkForgeCopyEngineToOutput</c>. Scoped to that specific group, rather than every
    /// <c>None</c> in the file, so a stray item added to some other, unrelated <c>ItemGroup</c>
    /// later cannot silently join the copy set this test compares against the packed set.
    /// </summary>
    private static IReadOnlyList<XElement> CopyGroupItems()
    {
        var props = XDocument.Load(RepoPath(PropsFile));
        var group = props.Descendants("ItemGroup")
            .SingleOrDefault(g => (g.Attribute("Condition")?.Value ?? string.Empty)
                .Contains("FalkForgeCopyEngineToOutput", StringComparison.Ordinal));

        Assert.NotNull(group);
        return [.. group.Descendants("None")];
    }

    private static string Attribute(XElement element, string name)
        => element.Attribute(name)?.Value ?? string.Empty;

    /// <summary>
    /// The file name an <c>Include</c> ends in. Both files write the payload as an MSBuild
    /// property followed by the file name (<c>$(FalkForgeEngineDir)FalkForge.Engine.exe</c>), and
    /// the property holds the directory separator, so the leading <c>$(...)</c> is dropped before
    /// the name is taken.
    /// </summary>
    private static string PayloadName(XElement item)
    {
        var include = Attribute(item, "Include");
        var propertyEnd = include.StartsWith("$(", StringComparison.Ordinal)
            ? include.IndexOf(')', StringComparison.Ordinal)
            : -1;

        if (propertyEnd >= 0)
            include = include[(propertyEnd + 1)..];

        return Path.GetFileName(include);
    }

    private static string RepoPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var full = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"Expected {relativePath} to exist under {dir.FullName}.");
        return full;
    }
}
