namespace FalkForge.Integration.Tests.Packaging;

using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// A consumer of the prebuilt packages can only get a pinned engine if they can build one, and they
/// can only build one if every file the engine's compilation needs is in the package. A package that
/// is missing one .cs file fails at the consumer's first build, not here, so pin the contents.
/// </summary>
public sealed class EngineSourcePackageTests
{
    private static readonly Lazy<string> PackedPackage = new(PackSourcePackage);

    private const string SourcesPackageId = "FalkForge.Engine.Sources";

    [Fact]
    public void Package_CarriesEveryEngineAndCompanionSourceFile()
    {
        using var package = ZipFile.OpenRead(PackedNupkgPath());
        var entries = package.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A walk that finds zero .cs files makes the loop below a no-op that still passes, so this
        // test would go green on a package that carries no source at all. Assert the walk itself
        // found something before trusting what it did not complain about.
        var sourceFiles = RepoSourceFiles().ToList();
        Assert.NotEmpty(sourceFiles);

        foreach (var (project, relative) in sourceFiles)
        {
            var expected = $"tools/src/{project}/{relative}";
            Assert.True(entries.Contains(expected), $"missing from the package: {expected}");
        }
    }

    [Fact]
    public void Package_CarriesTheStopperMsBuildFiles()
    {
        // Without these, a consumer whose repository root sets TreatWarningsAsErrors or pins central
        // package versions silently reshapes the engine build. Measured: an empty Directory.Build.props
        // beside the project stops the upward walk (Part 0 of the plan).
        using var package = ZipFile.OpenRead(PackedNupkgPath());
        var entries = package.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("tools/src/Directory.Build.props", entries);
        Assert.Contains("tools/src/Directory.Build.targets", entries);
        Assert.Contains("tools/src/Directory.Packages.props", entries);
        Assert.Contains("tools/src/NuGet.config", entries);
        Assert.Equal(File.ReadAllText(Path.Combine(RepoRoot(), "global.json")),
            ReadPackedEntry(package, "tools/src/global.json"));
        Assert.Contains("tools/src/FalkForge.Engine/packages.lock.json", entries);
        Assert.Contains("tools/src/FalkForge.Engine.Elevation/packages.lock.json", entries);
        var analyzerPolicy = ReadPackedEntry(package, "tools/src/.editorconfig");
        Assert.Equal(File.ReadAllText(Path.Combine(RepoRoot(), ".editorconfig")), analyzerPolicy);
        Assert.Contains("root = true", analyzerPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagedProjects_ReferencePackagesNotSiblingProjects()
    {
        // A ProjectReference in the shipped csproj points at a path that does not exist on a
        // consumer's disk, so the first build fails with a confusing MSBuild error. Reads the REAL
        // packed csproj -- the substituted copy the nupkg actually ships -- not the checked-in
        // template: the template still carries the literal $(FalkForgePackageVersion) placeholder,
        // so reading it here would let an empty or malformed substitution pass unnoticed.
        using var package = ZipFile.OpenRead(PackedNupkgPath());
        foreach (var name in new[] { "FalkForge.Engine", "FalkForge.Engine.Elevation" })
        {
            var text = ReadPackedEntry(package, $"tools/src/{name}/{name}.csproj");

            Assert.DoesNotContain("<ProjectReference", text, StringComparison.Ordinal);
            Assert.DoesNotContain("$(FalkForgePackageVersion)", text, StringComparison.Ordinal);
            Assert.Contains("<PackageReference Include=\"FalkForge.Engine.Protocol\"", text,
                StringComparison.Ordinal);

            // The bare version NuGet used to see here reads as a floor, not a pin: "1.2.3" resolves
            // to >=1.2.3. Exact bracket notation is what actually pins it.
            var match = Regex.Match(text, @"FalkForge\.Engine\.Protocol""\s+Version=""(\[[^""]+\])""");
            Assert.True(match.Success,
                $"FalkForge.Engine.Protocol's PackageReference in {name}.csproj is not an exact " +
                $"bracketed version:\n{text}");
            Assert.NotEqual("[]", match.Groups[1].Value);
        }
    }

    [Fact]
    public void PackagedProjects_DoNotCompileViaTheDefaultGlob()
    {
        // Once restored, the packaged csproj lives under the machine-wide global NuGet packages
        // folder (%USERPROFILE%\.nuget\packages), which is user-writable and shared by every
        // project on the machine -- see FalkForge.Engine.Sources/build/FalkForge.Engine.Sources.props.
        // NuGet never re-verifies an already-extracted package, so a .cs file dropped into that
        // directory after the fact would silently join the next build if the project still compiled
        // via the SDK's default **/*.cs glob. EnableDefaultCompileItems=false plus an explicit,
        // enumerated <Compile> list closes that: a planted file sits on disk but not in the list, so
        // it never compiles in.
        using var package = ZipFile.OpenRead(PackedNupkgPath());
        foreach (var name in new[] { "FalkForge.Engine", "FalkForge.Engine.Elevation" })
        {
            var text = ReadPackedEntry(package, $"tools/src/{name}/{name}.csproj");

            Assert.Contains("<EnableDefaultCompileItems>false</EnableDefaultCompileItems>", text,
                StringComparison.Ordinal);
            Assert.DoesNotContain("FALKFORGE_COMPILE_ITEMS", text, StringComparison.Ordinal);
            Assert.Matches(new Regex(@"<Compile Include=""[^""]+\.cs"" />"), text);
        }
    }

    /// <summary>
    /// The tests above check zip entries and grep csproj text, so a package that is missing one .cs
    /// file, or whose rewritten csproj does not restore, still passes them and fails at the
    /// consumer's first build. This test extracts the real package and builds both packaged projects
    /// against their real sibling packages (FalkForge.Engine.Protocol, FalkForge.Platform.Windows,
    /// FalkForge.Compiler.Msi and their own closure) from the local feed <c>scripts/pack.ps1</c>
    /// produces, mirroring the gating <see cref="FalkForge.Integration.Tests.NuGetConsumerEndToEndTests"/> already uses: those
    /// sibling packages are pre-release and, even where nuget.org already carries a same-numbered
    /// package from an earlier point in this pre-release cycle, that copy can be stale — measured:
    /// the published FalkForge.Engine.Protocol 0.5.0-beta.7 predates TrustPolicy and
    /// HashBoundFileResult, so restore is pinned to the local feed exclusively, not merely offered
    /// it as an extra source. Managed build only — a full NativeAOT publish needs the C++
    /// toolchain and would make the suite machine-dependent.
    /// </summary>
    [Fact]
    public void PackagedSource_RestoresAndBuildsAgainstItsRealSiblingPackages()
    {
        var feed = FindLocalFeedWithSourcesPackage();
        if (Environment.GetEnvironmentVariable("FALKFORGE_REQUIRE_SOURCE_FEED") == "1")
            Assert.NotNull(feed);
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        // Always rebuild the source package from this checkout; the feed supplies dependencies only.
        var nupkg = PackedNupkgPath();
        var extractDir = Path.Combine(Path.GetTempPath(), "fk-srcbuild-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(nupkg, extractDir);

        var engineCsproj = Path.Combine(extractDir, "tools", "src", "FalkForge.Engine", "FalkForge.Engine.csproj");
        var elevationCsproj = Path.Combine(
            extractDir, "tools", "src", "FalkForge.Engine.Elevation", "FalkForge.Engine.Elevation.csproj");

        // Isolated NuGet package cache: without it, a package of the same pre-release version
        // already sitting in the developer's real global cache from an earlier run would shadow the
        // freshly packed one and this test would prove nothing (same reasoning as
        // NuGetConsumerEndToEndTests's isolated NUGET_PACKAGES).
        var packagesPath = Path.Combine(Path.GetTempPath(), "fk-srcbuild-pkgs-" + Guid.NewGuid().ToString("N"));
        var objDir = Path.Combine(Path.GetTempPath(), "fk-srcbuild-obj-" + Guid.NewGuid().ToString("N"));

        var testNuGetConfig = BuildTestNuGetConfigFromTheShippedFile(feed);

        var engineExit = ProcessRunner.Run("dotnet",
            [
                "build", engineCsproj, "--nologo",
                $"-p:BaseIntermediateOutputPath={Path.Combine(objDir, "engine")}{Path.DirectorySeparatorChar}",
                $"-p:RestoreConfigFile={testNuGetConfig}",
                $"-p:RestorePackagesPath={packagesPath}",
                "-p:TreatWarningsAsErrors=true",
            ],
            out var engineOutput);
        Assert.True(engineExit == 0, $"packaged engine source failed to build:\n{engineOutput}");

        var elevationExit = ProcessRunner.Run("dotnet",
            [
                "build", elevationCsproj, "--nologo",
                $"-p:BaseIntermediateOutputPath={Path.Combine(objDir, "elevation")}{Path.DirectorySeparatorChar}",
                $"-p:RestoreConfigFile={testNuGetConfig}",
                $"-p:RestorePackagesPath={packagesPath}",
                "-p:TreatWarningsAsErrors=true",
            ],
            out var elevationOutput);
        Assert.True(elevationExit == 0, $"packaged elevation companion source failed to build:\n{elevationOutput}");

        // Proves the stopper files did their job in a real restore, not only in a zip listing: both
        // TrustedKeys.targets imports ran and generated their source.
        var generated = Directory.GetFiles(objDir, "TrustedKeys.g.cs", SearchOption.AllDirectories);
        Assert.Equal(2, generated.Length);

        // Proves the analyzer set Directory.Build.props puts back actually restored into this build,
        // not only that the props file exists. project.assets.json records every package that was
        // resolved for the project, analyzers included.
        var engineAssets = File.ReadAllText(Path.Combine(objDir, "engine", "project.assets.json"));
        Assert.DoesNotContain("SonarAnalyzer.CSharp", engineAssets, StringComparison.Ordinal);
        Assert.Contains("SecurityCodeScan.VS2019", engineAssets, StringComparison.Ordinal);
        Assert.Contains("IDisposableAnalyzers", engineAssets, StringComparison.Ordinal);
        Assert.Contains("Meziantou.Analyzer", engineAssets, StringComparison.Ordinal);
        Assert.Contains("Microsoft.VisualStudio.Threading.Analyzers", engineAssets, StringComparison.Ordinal);

        // A changed dependency declaration must fail, not rewrite the publisher's first lock.
        var lockPath = Path.Combine(Path.GetDirectoryName(engineCsproj)!, "packages.lock.json");
        var originalLock = File.ReadAllText(lockPath);
        var projectText = File.ReadAllText(engineCsproj);
        var projectDocument = System.Xml.Linq.XDocument.Parse(projectText);
        projectDocument.Descendants("PackageReference").First()
            .SetAttributeValue("Version", "[0.0.0-ledger-drift]");
        var mutatedProject = projectDocument.ToString();
        Assert.NotEqual(projectText, mutatedProject);
        File.WriteAllText(engineCsproj, mutatedProject);
        var driftExit = ProcessRunner.Run("dotnet",
            ["restore", engineCsproj, "--force",
             $"-p:RestoreConfigFile={testNuGetConfig}", $"-p:RestorePackagesPath={packagesPath}"],
            out var driftOutput);
        Assert.NotEqual(0, driftExit);
        Assert.Contains("NU1004", driftOutput, StringComparison.Ordinal);
        Assert.Equal(originalLock, File.ReadAllText(lockPath));
    }

    /// <summary>
    /// Builds the restore config this test uses from the SHIPPED NuGet.config's own text, adding
    /// only the local feed this session's freshly packed FalkForge.* siblings live in. Earlier this
    /// test wrote an entirely independent config, so deleting the shipped file's &lt;clear /&gt; or
    /// its packageSourceMapping left every test in this class green -- nothing here read the shipped
    /// file at all. The Assert.Contains calls below fail loudly, before any restore runs, if a future
    /// edit strips either one out.
    /// </summary>
    private static string BuildTestNuGetConfigFromTheShippedFile(string feed)
    {
        const string NugetOrgSourceLine =
            "<add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />";
        const string NugetOrgMappingOpen = "<packageSource key=\"nuget.org\">";

        var shippedConfigPath = Path.Combine(
            RepoRoot(), "src", "FalkForge.Engine.Sources", "src", "NuGet.config");
        var shippedConfigText = File.ReadAllText(shippedConfigPath);

        Assert.Contains("<clear />", shippedConfigText, StringComparison.Ordinal);
        Assert.Contains("<packageSourceMapping>", shippedConfigText, StringComparison.Ordinal);
        Assert.Contains(NugetOrgSourceLine, shippedConfigText, StringComparison.Ordinal);
        Assert.Contains(NugetOrgMappingOpen, shippedConfigText, StringComparison.Ordinal);

        // Package source mapping picks the most specific pattern for each package id regardless of
        // which entry declares it, so adding "FalkForge.*" -> the local feed here narrows where
        // FalkForge.* packages resolve from without loosening the shipped file's own "*" -> nuget.org
        // restriction for anything else -- the NativeAOT toolchain packages PublishAot references
        // included (measured: dropping nuget.org from the source list entirely breaks their restore).
        var withLocalSource = shippedConfigText.Replace(
            NugetOrgSourceLine,
            NugetOrgSourceLine + Environment.NewLine +
                $"    <add key=\"falkforge-local\" value=\"{feed}\" />",
            StringComparison.Ordinal);
        var withLocalMapping = withLocalSource.Replace(
            NugetOrgMappingOpen,
            "<packageSource key=\"falkforge-local\">" + Environment.NewLine +
                "      <package pattern=\"FalkForge.*\" />" + Environment.NewLine +
                "    </packageSource>" + Environment.NewLine +
                "    " + NugetOrgMappingOpen,
            StringComparison.Ordinal);

        Assert.Contains($"key=\"falkforge-local\" value=\"{feed}\"", withLocalMapping, StringComparison.Ordinal);
        Assert.Contains("<packageSource key=\"falkforge-local\">", withLocalMapping, StringComparison.Ordinal);

        var path = Path.Combine(Path.GetTempPath(), "fk-srcbuild-nuget-" + Guid.NewGuid().ToString("N") + ".config");
        File.WriteAllText(path, withLocalMapping);
        return path;
    }

    private static string ReadPackedEntry(ZipArchive package, string entryPath)
    {
        var entry = package.GetEntry(entryPath);
        Assert.True(entry is not null, $"missing from the package: {entryPath}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static IEnumerable<(string Project, string Relative)> RepoSourceFiles()
    {
        foreach (var project in new[] { "FalkForge.Engine", "FalkForge.Engine.Elevation" })
        {
            var root = Path.Combine(RepoRoot(), "src", project);
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return (project, relative);
            }
        }
    }

    private static string PackedNupkgPath() => PackedPackage.Value;

    private static string PackSourcePackage()
    {
        var feed = FindLocalFeedWithSourcesPackage();
        if (Environment.GetEnvironmentVariable("FALKFORGE_REQUIRE_SOURCE_FEED") == "1")
            Assert.NotNull(feed);
        Assert.SkipUnless(feed is not null, FeedSkipReason);
        // Packs fresh source into a temp folder; the matching sibling feed supplies lock inputs.
        var outDir = Path.Combine(Path.GetTempPath(), "fk-srcpack-" + Guid.NewGuid().ToString("N"));
        var objDir = Path.Combine(Path.GetTempPath(), "fk-srcpack-obj-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(
            RepoRoot(), "src", "FalkForge.Engine.Sources", "FalkForge.Engine.Sources.csproj");
        var exit = ProcessRunner.Run("dotnet",
            ["pack", project, "--nologo", "-o", outDir,
             $"-p:BaseIntermediateOutputPath={objDir}{Path.DirectorySeparatorChar}",
             $"-p:FalkForgeSourceLockFeed={feed}"],
            out var output);
        Assert.True(exit == 0, $"pack failed:\n{output}");
        return Directory.GetFiles(outDir, "*.nupkg").Single();
    }

    /// <summary>
    /// The local feed produced by <c>scripts/pack.ps1</c>, or null when it (or the source package)
    /// is absent. Null gates <see cref="PackagedSource_RestoresAndBuildsAgainstItsRealSiblingPackages"/>
    /// with an explicit skip, the same convention <see cref="FalkForge.Integration.Tests.NuGetConsumerEndToEndTests"/> uses.
    /// </summary>
    private static string? FindLocalFeedWithSourcesPackage()
    {
        var feed = Environment.GetEnvironmentVariable("FALKFORGE_SOURCE_TEST_FEED")
            ?? Path.Combine(RepoRoot(), "artifacts", "nuget");
        if (!Directory.Exists(feed))
            return null;

        var properties = System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.props"));
        var prefix = properties.Descendants("VersionPrefix").Single().Value;
        var suffix = properties.Descendants("VersionSuffix").Single().Value;
        var version = string.IsNullOrEmpty(suffix) ? prefix : $"{prefix}-{suffix}";
        return File.Exists(Path.Combine(feed, $"FalkForge.Engine.Protocol.{version}.nupkg")) ? feed : null;
    }

    private const string FeedSkipReason =
        "Local NuGet feed with " + SourcesPackageId + " not found at artifacts/nuget — run " +
        "scripts/pack.ps1 first. This test restores the packaged engine/companion source against " +
        "its real sibling packages rather than nuget.org (which does not have this pre-release " +
        "version), so it needs the whole solution packed.";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    /// <summary>Runs a process, capturing combined stdout+stderr, and returns its exit code.</summary>
    private static class ProcessRunner
    {
        internal static int Run(string fileName, string[] arguments, out string output)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(arguments[1])!,
            };
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi)!;
            output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
