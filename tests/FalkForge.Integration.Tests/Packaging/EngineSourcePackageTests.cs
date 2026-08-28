namespace FalkForge.Integration.Tests.Packaging;

using System.Diagnostics;
using System.IO.Compression;
using Xunit;

/// <summary>
/// A consumer of the prebuilt packages can only get a pinned engine if they can build one, and they
/// can only build one if every file the engine's compilation needs is in the package. A package that
/// is missing one .cs file fails at the consumer's first build, not here, so pin the contents.
/// </summary>
public sealed class EngineSourcePackageTests
{
    private const string SourcesPackageId = "FalkForge.Engine.Sources";

    [Fact]
    public void Package_CarriesEveryEngineAndCompanionSourceFile()
    {
        using var package = ZipFile.OpenRead(PackedNupkgPath());
        var entries = package.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (project, relative) in RepoSourceFiles())
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
    }

    [Fact]
    public void PackagedProjects_ReferencePackagesNotSiblingProjects()
    {
        // A ProjectReference in the shipped csproj points at a path that does not exist on a
        // consumer's disk, so the first build fails with a confusing MSBuild error.
        foreach (var name in new[] { "FalkForge.Engine", "FalkForge.Engine.Elevation" })
        {
            var text = File.ReadAllText(Path.Combine(
                RepoRoot(), "src", "FalkForge.Engine.Sources", "src", name, $"{name}.csproj"));
            Assert.DoesNotContain("<ProjectReference", text, StringComparison.Ordinal);
            Assert.Contains("<PackageReference Include=\"FalkForge.Engine.Protocol\"", text,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The three tests above check zip entries and grep csproj text, so a package that is missing
    /// one .cs file, or whose rewritten csproj does not restore, still passes them and fails at the
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
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        var nupkg = Directory.GetFiles(feed, SourcesPackageId + ".*.nupkg").Single();
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

        // Package-source-mapped config, the same local-feed convention NuGetConsumerEndToEndTests
        // uses, refined with mapping. An additional source is not enough on its own: measured here,
        // FalkForge.Engine.Protocol 0.5.0-beta.7 is already published on nuget.org from an earlier
        // point in this same pre-release cycle, and it is stale (it predates
        // TrustPolicy/HashBoundFileResult). With the packaged NuGet.config's nuget.org source still
        // active and the local feed merely added via RestoreAdditionalSources, restore picked the
        // stale nuget.org copy and the build failed with CS0234. But dropping nuget.org entirely
        // (measured) breaks restore of the NativeAOT toolchain packages (Microsoft.DotNet.ILCompiler
        // and friends) the SDK references implicitly whenever PublishAot is set, regardless of
        // whether publish ever runs. Package source mapping routes every FalkForge.* id to the local
        // feed exclusively while leaving everything else on nuget.org.
        var testNuGetConfig = Path.Combine(Path.GetTempPath(), "fk-srcbuild-nuget-" + Guid.NewGuid().ToString("N") + ".config");
        File.WriteAllText(testNuGetConfig, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="falkforge-local" value="{feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="falkforge-local">
                  <package pattern="FalkForge.*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

        var engineExit = ProcessRunner.Run("dotnet",
            [
                "build", engineCsproj, "--nologo",
                $"-p:BaseIntermediateOutputPath={Path.Combine(objDir, "engine")}{Path.DirectorySeparatorChar}",
                $"-p:RestoreConfigFile={testNuGetConfig}",
                $"-p:RestorePackagesPath={packagesPath}",
            ],
            out var engineOutput);
        Assert.True(engineExit == 0, $"packaged engine source failed to build:\n{engineOutput}");

        var elevationExit = ProcessRunner.Run("dotnet",
            [
                "build", elevationCsproj, "--nologo",
                $"-p:BaseIntermediateOutputPath={Path.Combine(objDir, "elevation")}{Path.DirectorySeparatorChar}",
                $"-p:RestoreConfigFile={testNuGetConfig}",
                $"-p:RestorePackagesPath={packagesPath}",
            ],
            out var elevationOutput);
        Assert.True(elevationExit == 0, $"packaged elevation companion source failed to build:\n{elevationOutput}");

        // Proves the stopper files did their job in a real restore, not only in a zip listing: both
        // TrustedKeys.targets imports ran and generated their source.
        var generated = Directory.GetFiles(objDir, "TrustedKeys.g.cs", SearchOption.AllDirectories);
        Assert.Equal(2, generated.Length);
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

    private static string PackedNupkgPath()
    {
        // Packs into a temp folder so the test never depends on a prior scripts/pack.ps1 run.
        var outDir = Path.Combine(Path.GetTempPath(), "fk-srcpack-" + Guid.NewGuid().ToString("N"));
        var objDir = Path.Combine(Path.GetTempPath(), "fk-srcpack-obj-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(
            RepoRoot(), "src", "FalkForge.Engine.Sources", "FalkForge.Engine.Sources.csproj");
        var exit = ProcessRunner.Run("dotnet",
            ["pack", project, "--nologo", "-o", outDir,
             $"-p:BaseIntermediateOutputPath={objDir}{Path.DirectorySeparatorChar}"],
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
        var feed = Path.Combine(RepoRoot(), "artifacts", "nuget");
        if (!Directory.Exists(feed))
            return null;

        var sources = Directory.GetFiles(feed, SourcesPackageId + ".*.nupkg").SingleOrDefault();
        return sources is null ? null : feed;
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
