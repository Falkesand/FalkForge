using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Pins the onboarding contract of the <c>FalkForge</c> meta-package: one
/// <c>dotnet add package FalkForge</c> must transitively deliver everything a code-first
/// installer author needs — the fluent API, both compilers, localization, the extension set,
/// AND the engine runtime package whose build props make bundles runnable with zero manual
/// engine setup. The meta-package ships no library of its own; it is pure dependency wiring.
/// <para>
/// Gated on the local feed produced by <c>scripts/pack.ps1</c> (explicit skip, never silent)
/// because packing requires the multi-minute NativeAOT engine publish.
/// </para>
/// </summary>
public sealed class MetaPackageTests
{
    private const string MetaPackageId = "FalkForge";

    /// <summary>
    /// The dependency set one <c>dotnet add package FalkForge</c> must pull in. Deliberately
    /// excludes the WPF UI stack (net10.0-windows, heavy — custom-UI authors reference
    /// FalkForge.Ui explicitly), the experimental MSIX compiler, the decompiler, plugins,
    /// and SignServer remote signing (niche, opt-in).
    /// </summary>
    private static readonly string[] ExpectedDependencyIds =
    [
        "FalkForge.Core",
        "FalkForge.Compiler.Msi",
        "FalkForge.Compiler.Bundle",
        "FalkForge.Localization",
        "FalkForge.Extensibility",
        "FalkForge.Extensions.Util",
        "FalkForge.Extensions.Dependency",
        "FalkForge.Extensions.Firewall",
        "FalkForge.Extensions.DotNet",
        "FalkForge.Extensions.Iis",
        "FalkForge.Extensions.Sql",
        "FalkForge.Extensions.Driver",
        "FalkForge.Extensions.Http",
        "FalkForge.Engine.Runtime.win-x64"
    ];

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    private static string? FindFeed()
    {
        var root = FindRepoRoot();
        if (root is null)
            return null;

        var feed = Path.Combine(root, "artifacts", "nuget");
        // Gate on the feed being a full pack.ps1 output (tool + engine runtime present); a feed
        // produced by this change's pack.ps1 must then also contain the meta-package, so a
        // missing meta-package inside a complete feed is a genuine failure, not a skip.
        if (!Directory.Exists(feed) ||
            Directory.GetFiles(feed, "FalkForge.Tool.*.nupkg").Length == 0 ||
            Directory.GetFiles(feed, "FalkForge.Engine.Runtime.win-x64.*.nupkg").Length == 0)
        {
            return null;
        }

        return feed;
    }

    private const string FeedSkipReason =
        "Local NuGet feed not found at artifacts/nuget — run scripts/pack.ps1 first. This gate " +
        "exists because packing requires the multi-minute NativeAOT engine publish.";

    private static string FindMetaNupkg(string feed)
    {
        // "FalkForge.*.nupkg" also matches every granular package; the meta-package is the one
        // whose file name is FalkForge.<version>.nupkg — i.e. the segment after the id parses
        // as a number-led version.
        var candidates = Directory.GetFiles(feed, MetaPackageId + ".*.nupkg")
            .Where(f =>
            {
                var rest = Path.GetFileName(f)[(MetaPackageId.Length + 1)..];
                return rest.Length > 0 && char.IsAsciiDigit(rest[0]);
            })
            .ToArray();

        Assert.True(candidates.Length == 1,
            $"expected exactly one {MetaPackageId}.<version>.nupkg in {feed}, found " +
            $"[{string.Join(", ", candidates.Select(Path.GetFileName))}] — the meta-package " +
            "must be produced by dotnet pack of the solution (scripts/pack.ps1).");
        return candidates[0];
    }

    [Fact]
    public void MetaPackage_DeclaresBatteriesIncludedDependencySet()
    {
        var feed = FindFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        using var archive = ZipFile.OpenRead(FindMetaNupkg(feed));

        var nuspecEntry = archive.Entries.Single(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        XDocument nuspec;
        using (var stream = nuspecEntry.Open())
        {
            nuspec = XDocument.Load(stream);
        }

        XNamespace ns = nuspec.Root!.GetDefaultNamespace();
        Assert.Equal(MetaPackageId, nuspec.Descendants(ns + "id").Single().Value);

        var declared = nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in ExpectedDependencyIds)
        {
            Assert.True(declared.Contains(expected),
                $"meta-package must depend on {expected}; declared: [{string.Join(", ", declared)}]");
        }

        // Every dependency must pin the same single-source version as the meta-package itself,
        // so one feed restore resolves one coherent FalkForge version.
        var metaVersion = nuspec.Descendants(ns + "version").First().Value;
        foreach (var dependency in nuspec.Descendants(ns + "dependency"))
        {
            Assert.Equal(metaVersion, dependency.Attribute("version")!.Value.Trim('[', ']'));
        }
    }

    [Fact]
    public void MetaPackage_ShipsNoLibraryOfItsOwn()
    {
        var feed = FindFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        using var archive = ZipFile.OpenRead(FindMetaNupkg(feed));

        // Pure dependency wiring: no lib/, no build props hijacking the consumer, no tools
        // payload. The engine binaries travel in FalkForge.Engine.Runtime.win-x64, never here.
        Assert.DoesNotContain(archive.Entries, e =>
            e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith("build/", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith("tools/", StringComparison.OrdinalIgnoreCase));
    }
}
