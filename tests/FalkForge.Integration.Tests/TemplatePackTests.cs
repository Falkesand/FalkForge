using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Pins the structure of the <c>FalkForge.Templates</c> template pack — the
/// <c>dotnet new install FalkForge.Templates</c> → <c>dotnet new falkforge-msi</c> onboarding
/// path. The instantiate-restore-build-run proof against a real feed lives in
/// <c>OnboardingEndToEndTests</c>; these tests validate the template sources in the repo (fast,
/// no feed) and the packed shape (feed-gated).
/// </summary>
public sealed partial class TemplatePackTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "repo root with FalkForge.slnx not found");
        return dir.FullName;
    }

    private static string TemplateContentDir(string shortName) =>
        Path.Combine(RepoRoot(), "src", "FalkForge.Templates", "content", shortName);

    private static JsonDocument LoadTemplateJson(string shortName)
    {
        var path = Path.Combine(TemplateContentDir(shortName), ".template.config", "template.json");
        Assert.True(File.Exists(path), $"expected template manifest at {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// The single-source version from the root Directory.Build.props. Comparing against it keeps
    /// the template's default FalkForge package version from silently drifting on a version bump.
    /// </summary>
    private static string SingleSourceVersion()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "Directory.Build.props"));
        var prefix = Regex.Match(props, "<VersionPrefix>([^<]+)</VersionPrefix>").Groups[1].Value;
        var suffix = Regex.Match(props, "<VersionSuffix>([^<]+)</VersionSuffix>").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(prefix), "VersionPrefix not found in Directory.Build.props");
        return suffix.Length > 0 ? $"{prefix}-{suffix}" : prefix;
    }

    [Theory]
    [InlineData("falkforge-msi")]
    [InlineData("falkforge-bundle")]
    public void Template_HasValidManifest_AndMetaPackageReferenceAtSingleSourceVersion(string shortName)
    {
        using var manifest = LoadTemplateJson(shortName);
        var root = manifest.RootElement;

        Assert.Equal(shortName, root.GetProperty("shortName").GetString());
        Assert.Equal("project",
            root.GetProperty("tags").GetProperty("type").GetString());

        // sourceName drives project-file renaming: the content project file must carry it.
        var sourceName = root.GetProperty("sourceName").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sourceName));
        var csprojPath = Path.Combine(TemplateContentDir(shortName), sourceName + ".csproj");
        Assert.True(File.Exists(csprojPath), $"expected content project at {csprojPath}");

        // The template references the ONE meta-package — the whole onboarding story — and its
        // default version is pinned to the single source of truth so a version bump cannot
        // silently leave templates referencing a stale (or unpublished) FalkForge.
        var csproj = File.ReadAllText(csprojPath);
        Assert.Contains("""<PackageReference Include="FalkForge" Version="FALKFORGE-VERSION" />""",
            csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("FalkForge.Core", csproj, StringComparison.Ordinal);

        var versionSymbol = root.GetProperty("symbols").GetProperty("FalkForgeVersion");
        Assert.Equal("FALKFORGE-VERSION", versionSymbol.GetProperty("replaces").GetString());
        Assert.Equal(SingleSourceVersion(), versionSymbol.GetProperty("defaultValue").GetString());

        // Both templates must compile to a runnable installer program.
        var program = File.ReadAllText(Path.Combine(TemplateContentDir(shortName), "Program.cs"));
        Assert.Contains(shortName == "falkforge-msi" ? "Installer.Build(args" : "Installer.BuildBundle(args",
            program, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleTemplate_RegeneratesItsGuidsPerInstantiation()
    {
        using var manifest = LoadTemplateJson("falkforge-bundle");
        var declaredGuids = manifest.RootElement.GetProperty("guids").EnumerateArray()
            .Select(g => Guid.Parse(g.GetString()!))
            .ToList();

        // BundleId + UpgradeCode: shared literals across instantiations would make every
        // project created from this template upgrade-collide on end-user machines, so both
        // source GUIDs must be registered for per-instantiation regeneration.
        var program = File.ReadAllText(Path.Combine(TemplateContentDir("falkforge-bundle"), "Program.cs"));
        var sourceGuids = GuidLiteral().Matches(program)
            .Select(m => Guid.Parse(m.Groups[1].Value))
            .ToList();

        Assert.True(sourceGuids.Count >= 2, "bundle template must embed BundleId and UpgradeCode");
        foreach (var guid in sourceGuids)
            Assert.Contains(guid, declaredGuids);
    }

    [Fact]
    public void PackedTemplatePack_IsATemplatePackageCarryingBothTemplates()
    {
        var feed = Path.Combine(RepoRoot(), "artifacts", "nuget");
        var nupkg = Directory.Exists(feed)
            ? Directory.GetFiles(feed, "FalkForge.Templates.*.nupkg").SingleOrDefault()
            : null;
        Assert.SkipUnless(nupkg is not null,
            "Local NuGet feed with FalkForge.Templates not found at artifacts/nuget — run " +
            "scripts/pack.ps1 first (gated because packing needs the NativeAOT engine publish).");

        using var archive = ZipFile.OpenRead(nupkg);

        var nuspecEntry = archive.Entries.Single(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(nuspecEntry.Open());
        var nuspec = reader.ReadToEnd();
        Assert.Contains("""<packageType name="Template" """, nuspec, StringComparison.Ordinal);

        foreach (var shortName in new[] { "falkforge-msi", "falkforge-bundle" })
        {
            Assert.NotNull(archive.GetEntry($"content/{shortName}/.template.config/template.json"));
            Assert.NotNull(archive.GetEntry($"content/{shortName}/Program.cs"));
            Assert.NotNull(archive.GetEntry($"content/{shortName}/payload/readme.txt"));
        }
    }

    [GeneratedRegex("""new Guid\("([0-9a-fA-F-]{36})"\)""")]
    private static partial Regex GuidLiteral();
}
