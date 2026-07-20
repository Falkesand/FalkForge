using System.Diagnostics;
using System.Text.RegularExpressions;
using FalkForge.Cli;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Proves the ONBOARDING chain end to end against the local feed: a developer with no repo
/// checkout goes from zero to a real, runnable installer via either <c>forge init</c> or
/// <c>dotnet new falkforge-*</c>, with everything restored from ONE
/// <c>FalkForge</c> meta-package reference.
/// <para>What this genuinely exercises (previously unproven for the code-first path):</para>
/// <list type="number">
///   <item><description>The meta-package's transitive restore: scaffolded/templated projects
///   reference only <c>FalkForge</c>; compilers, extensions, and the engine runtime arrive
///   transitively from the feed.</description></item>
///   <item><description>Engine resolution with no environment variable and no repo: the
///   engine-runtime package's buildTransitive props land the NativeAOT engine in the consumer's
///   build output, where the bundle compiler's beside-host probe finds it — asserted by
///   byte-for-byte self-extraction of the produced bundle.</description></item>
/// </list>
/// <para>
/// NuGet isolation: a private <c>NUGET_PACKAGES</c> cache and a config whose FalkForge packages
/// can ONLY come from the local feed (package source mapping). nuget.org stays available for
/// third-party transitive dependencies (e.g. Octopus.Octodiff via FalkForge.Compiler.Bundle),
/// exactly like a real consumer's environment.
/// </para>
/// <para>
/// Gated on the feed produced by <c>scripts/pack.ps1</c> with an explicit skip (never silent):
/// packing requires the multi-minute NativeAOT engine publish.
/// </para>
/// </summary>
public sealed class OnboardingEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public OnboardingEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkOnboardE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: a straggling process may still hold a handle briefly.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort rationale as above.
        }
    }

    // ---- feed gate ----

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FalkForge.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    /// <summary>
    /// The local feed, or null (→ explicit skip) unless it carries every package the
    /// onboarding chain consumes: the meta-package, the engine runtime it references, and the
    /// template pack.
    /// </summary>
    private static string? FindOnboardingFeed()
    {
        var root = FindRepoRoot();
        if (root is null)
            return null;

        var feed = Path.Combine(root, "artifacts", "nuget");
        if (!Directory.Exists(feed))
            return null;

        // "FalkForge.*.nupkg" also matches every granular package; the meta-package is the one
        // whose file name continues with a number-led version segment.
        var hasMeta = Directory.GetFiles(feed, "FalkForge.*.nupkg")
            .Any(f => char.IsAsciiDigit(Path.GetFileName(f)["FalkForge.".Length]));
        return hasMeta &&
               Directory.GetFiles(feed, "FalkForge.Engine.Runtime.win-x64.*.nupkg").Length == 1 &&
               Directory.GetFiles(feed, "FalkForge.Templates.*.nupkg").Length == 1
            ? feed
            : null;
    }

    private const string FeedSkipReason =
        "Local NuGet feed with the FalkForge meta-package, FalkForge.Engine.Runtime.win-x64, " +
        "and FalkForge.Templates not found at artifacts/nuget — run scripts/pack.ps1 first. " +
        "This gate exists because packing requires the multi-minute NativeAOT engine publish.";

    /// <summary>
    /// Extracts the FalkForge meta-package's version from its nupkg filename in the feed, using
    /// the same "starts with a digit" rule <see cref="FindOnboardingFeed"/> already applies to
    /// tell the meta-package apart from every granular sub-package (Engine.Runtime, Templates,
    /// Compiler.*, ...) that shares its "FalkForge." prefix.
    /// </summary>
    private static bool TryGetOnboardingFeedVersion(string feed, out string? feedVersion)
    {
        var metaPackage = Directory.GetFiles(feed, "FalkForge.*.nupkg")
            .FirstOrDefault(f => char.IsAsciiDigit(Path.GetFileName(f)["FalkForge.".Length]));
        if (metaPackage is null)
        {
            feedVersion = null;
            return false;
        }

        feedVersion = Regex.Match(Path.GetFileName(metaPackage), @"^FalkForge\.(.+)\.nupkg$")
            .Groups[1].Value;
        return true;
    }

    /// <summary>
    /// Skips (never fails) an onboarding test whose local feed was packed at an older commit
    /// than the current scaffold version: <c>forge init</c> stamps
    /// <c>&lt;PackageReference Include="FalkForge" Version="{VersionInfo.CliVersion}" /&gt;</c>,
    /// so a stale feed can't restore it — an environment artifact of <c>scripts/pack.ps1</c> not
    /// having been re-run, not a product regression.
    /// </summary>
    private static void AssertFeedVersionMatchesScaffold(string feed)
    {
        var scaffoldVersion = VersionInfo.CliVersion.Split('+')[0];
        TryGetOnboardingFeedVersion(feed, out var feedVersion);
        Assert.SkipUnless(feedVersion == scaffoldVersion,
            $"Local feed version {feedVersion} lags the scaffold version {scaffoldVersion}; " +
            "re-pack with scripts/pack.ps1. (env artifact, not a regression)");
    }

    // ---- feed-version skip gate: must read the meta-package's own version, not a sub-package's ----

    [Fact]
    public void TryGetOnboardingFeedVersion_ReadsMetaPackage_NotAGranularSubPackage()
    {
        // Sub-packages (Engine.Runtime, Templates, Compiler.Bundle) share the "FalkForge." file
        // prefix and can legitimately carry a different version than the meta-package during a
        // partial re-pack; the skip gate must compare against the meta-package's version, not
        // whichever "FalkForge.*.nupkg" file happens to be found first.
        var feed = Path.Combine(_tempDir, "fake-feed");
        Directory.CreateDirectory(feed);
        foreach (var name in new[]
                 {
                     "FalkForge.1.2.3.nupkg",
                     "FalkForge.Engine.Runtime.win-x64.9.9.9.nupkg",
                     "FalkForge.Templates.9.9.9.nupkg",
                     "FalkForge.Compiler.Bundle.9.9.9.nupkg"
                 })
            File.WriteAllBytes(Path.Combine(feed, name), []);

        var found = TryGetOnboardingFeedVersion(feed, out var feedVersion);

        Assert.True(found);
        Assert.Equal("1.2.3", feedVersion);
    }

    [Fact]
    public void TryGetOnboardingFeedVersion_NoMetaPackageInFeed_ReturnsFalse()
    {
        // A feed holding only sub-packages (e.g. a broken partial pack) must not be mistaken for
        // a versioned meta-package — the caller needs a definite "no version" signal to skip on.
        var feed = Path.Combine(_tempDir, "fake-feed-no-meta");
        Directory.CreateDirectory(feed);
        File.WriteAllBytes(Path.Combine(feed, "FalkForge.Templates.1.2.3.nupkg"), []);

        var found = TryGetOnboardingFeedVersion(feed, out var feedVersion);

        Assert.False(found);
        Assert.Null(feedVersion);
    }

    // ---- plumbing ----

    private void WriteNuGetConfig(string directory, string feed)
    {
        Directory.CreateDirectory(directory);
        // Source mapping pins every FalkForge* package to the local feed — nothing FalkForge can
        // leak in from nuget.org — while third-party transitive dependencies restore from
        // nuget.org like any real consumer's.
        File.WriteAllText(Path.Combine(directory, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="falkforge-local" value="{feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="falkforge-local">
                  <package pattern="FalkForge*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
    }

    private Dictionary<string, string> IsolatedEnvironment() => new()
    {
        ["NUGET_PACKAGES"] = Path.Combine(_tempDir, "nuget-cache"),
        // The engine must resolve from the restored package's build output, never from an
        // ambient override or the developer machine's publish tree.
        ["FALKFORGE_ENGINE_STUB"] = ""
    };

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName, string[] arguments, IDictionary<string, string>? environment = null,
        string? workingDirectory = null, int timeoutMinutes = 10)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;
        if (environment is not null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(timeoutMinutes)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{Path.GetFileName(fileName)} did not exit within {timeoutMinutes} minutes. " +
                        $"stdout: {stdout} stderr: {stderr}");
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static int RunForgeInit(InitSettings settings)
    {
        var command = (ICommand<InitSettings>)new InitCommand(new SpectreConsoleOutput());
        var context = new CommandContext([], new NullRemainingArguments(), "init", null);
        return command.ExecuteAsync(context, settings, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    private sealed class NullRemainingArguments : IRemainingArguments
    {
        public ILookup<string, string?> Parsed { get; } =
            Array.Empty<string>().ToLookup(_ => string.Empty, _ => (string?)null);
        public IReadOnlyList<string> Raw { get; } = [];
    }

    private (string ProjectDir, string OutDir) BuildAndRunInstallerProject(
        string feed, string projectDir, string exeName)
    {
        WriteNuGetConfig(projectDir, feed);

        var (buildExit, buildOut, buildErr) = RunProcess("dotnet",
            ["build", projectDir], IsolatedEnvironment(), workingDirectory: projectDir);
        Assert.True(buildExit == 0,
            $"consumer build failed (exit {buildExit}). stdout: {buildOut} stderr: {buildErr}");

        // Real-compile proof done; now RUN the installer program. Working directory is the
        // project dir (payload/ resolves relatively, mirroring `dotnet run`); output goes to a
        // separate directory via the program's -o switch.
        var outDir = Path.Combine(projectDir, "installer-out");
        var exePath = Path.Combine(projectDir, "bin", "Debug", "net10.0-windows", exeName);
        Assert.True(File.Exists(exePath), $"expected built installer program at {exePath}");

        var (runExit, runOut, runErr) = RunProcess(exePath, ["-o", outDir],
            IsolatedEnvironment(), workingDirectory: projectDir, timeoutMinutes: 5);
        Assert.True(runExit == 0,
            $"installer program failed (exit {runExit}). stdout: {runOut} stderr: {runErr}");

        return (projectDir, outDir);
    }

    private static void AssertValidMsi(string msiPath, string expectedProductName)
    {
        Assert.True(File.Exists(msiPath), $"expected MSI at {msiPath}");
        Assert.True(new FileInfo(msiPath).Length > 4096, "MSI is implausibly small");

        // OLE compound-file magic — an MSI is a compound file.
        Span<byte> header = stackalloc byte[4];
        using (var stream = File.OpenRead(msiPath))
        {
            stream.ReadExactly(header);
        }
        Assert.Equal(new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }, header.ToArray());

        // Genuinely readable by msi.dll, carrying the scaffolded product identity.
        var inspection = MsiInspector.Inspect(msiPath);
        Assert.True(inspection.IsSuccess,
            inspection.IsFailure ? inspection.Error.Message : null);
        Assert.Equal(expectedProductName, inspection.Value.ProductName);

        // The scaffolded MSI carries the Start Menu shortcut the scaffold now emits — a
        // Shortcut table entry, not just a string in the source that never made it through
        // compilation.
        Assert.Contains("Shortcut", inspection.Value.TableNames);
    }

    private void AssertRunnableSelfExtractingBundle(string bundlePath, string msiPath)
    {
        // The PE front is a real engine, not a placeholder: MZ header and megabytes of
        // NativeAOT binary before the payload even starts.
        Span<byte> prefix = stackalloc byte[2];
        using (var stream = File.OpenRead(bundlePath))
        {
            stream.ReadExactly(prefix);
            Assert.Equal((byte)'M', prefix[0]);
            Assert.Equal((byte)'Z', prefix[1]);
            Assert.True(stream.Length > 1024 * 1024,
                $"bundle is {stream.Length:N0} bytes — too small to embed the NativeAOT engine");
        }

        // The produced exe genuinely self-extracts: the running stub is the embedded engine
        // reading its own TOC and writing the chained MSI back out byte-for-byte.
        var (listExit, listOut, listErr) = RunProcess(bundlePath, ["--extract-list"], timeoutMinutes: 5);
        Assert.True(listExit == 0, $"--extract-list failed (exit {listExit}). stderr: {listErr}");
        Assert.Contains("MainMsi", listOut, StringComparison.Ordinal);

        var extractDir = Path.Combine(_tempDir, $"extracted_{Guid.NewGuid():N}");
        var (extractExit, _, extractErr) = RunProcess(bundlePath, ["--extract", extractDir], timeoutMinutes: 5);
        Assert.True(extractExit == 0, $"--extract failed (exit {extractExit}). stderr: {extractErr}");

        var extractedMsi = Path.Combine(extractDir, "MainMsi", "MainMsi.dat");
        Assert.True(File.Exists(extractedMsi), $"expected extracted MSI at {extractedMsi}");
        Assert.Equal(File.ReadAllBytes(msiPath), File.ReadAllBytes(extractedMsi));
    }

    // ---- forge init → build → run ----

    [Fact]
    public void ForgeInit_MsiScaffold_RestoresFromFeed_BuildsAndProducesValidMsi()
    {
        var feed = FindOnboardingFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);
        AssertFeedVersionMatchesScaffold(feed);

        var projectDir = Path.Combine(_tempDir, "init-msi");
        var initExit = RunForgeInit(new InitSettings { OutputDir = projectDir, Name = "Onboard App" });
        Assert.Equal(ExitCodes.Success, initExit);

        var (_, outDir) = BuildAndRunInstallerProject(feed, projectDir, "Onboard_App.exe");

        var msiPath = Assert.Single(Directory.GetFiles(outDir, "*.msi"));
        AssertValidMsi(msiPath, "Onboard App");
    }

    [Fact]
    public void ForgeInit_BundleScaffold_EmbedsFeedEngine_AndSelfExtracts()
    {
        var feed = FindOnboardingFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);
        AssertFeedVersionMatchesScaffold(feed);

        var projectDir = Path.Combine(_tempDir, "init-bundle");
        var initExit = RunForgeInit(new InitSettings
        {
            OutputDir = projectDir,
            Type = "bundle",
            Name = "OnboardSuite"
        });
        Assert.Equal(ExitCodes.Success, initExit);

        var (_, outDir) = BuildAndRunInstallerProject(feed, projectDir, "OnboardSuite.exe");

        // The meta-package's transitive engine-runtime reference must have landed the engine
        // in the consumer's build output — that is where the bundle compiler resolved it from.
        var engineInOutput = Path.Combine(
            projectDir, "bin", "Debug", "net10.0-windows", "engine", "FalkForge.Engine.exe");
        Assert.True(File.Exists(engineInOutput),
            "engine must be copied into the consumer's build output by the engine-runtime package props");

        var bundlePath = Assert.Single(Directory.GetFiles(outDir, "*.exe"));
        var msiPath = Assert.Single(Directory.GetFiles(outDir, "*.msi"));
        AssertRunnableSelfExtractingBundle(bundlePath, msiPath);
    }

    // ---- dotnet new install → dotnet new → build → run ----

    private string InstallTemplatesIntoIsolatedHive(string feed)
    {
        var workDir = Path.Combine(_tempDir, "template-work");
        WriteNuGetConfig(workDir, feed);
        var hive = Path.Combine(_tempDir, "template-hive");

        var templateVersion = Regex.Match(
            Path.GetFileName(Directory.GetFiles(feed, "FalkForge.Templates.*.nupkg").Single()),
            @"^FalkForge\.Templates\.(.+)\.nupkg$").Groups[1].Value;

        var (installExit, installOut, installErr) = RunProcess("dotnet",
            ["new", "install", $"FalkForge.Templates::{templateVersion}", "--debug:custom-hive", hive],
            IsolatedEnvironment(), workingDirectory: workDir);
        Assert.True(installExit == 0,
            $"dotnet new install failed (exit {installExit}). stdout: {installOut} stderr: {installErr}");

        return hive;
    }

    private string InstantiateTemplate(string hive, string shortName, string projectName, string productName)
    {
        var projectDir = Path.Combine(_tempDir, projectName);
        var (newExit, newOut, newErr) = RunProcess("dotnet",
            ["new", shortName, "--name", projectName, "--ProductName", productName,
             "--output", projectDir, "--debug:custom-hive", hive],
            IsolatedEnvironment(), workingDirectory: _tempDir, timeoutMinutes: 5);
        Assert.True(newExit == 0,
            $"dotnet new {shortName} failed (exit {newExit}). stdout: {newOut} stderr: {newErr}");
        return projectDir;
    }

    [Fact]
    public void DotnetNewMsiTemplate_FromFeed_BuildsAndProducesValidMsi()
    {
        var feed = FindOnboardingFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        var hive = InstallTemplatesIntoIsolatedHive(feed);
        var projectDir = InstantiateTemplate(hive, "falkforge-msi", "TplMsiApp", "Template App");

        var (_, outDir) = BuildAndRunInstallerProject(feed, projectDir, "TplMsiApp.exe");

        var msiPath = Assert.Single(Directory.GetFiles(outDir, "*.msi"));
        AssertValidMsi(msiPath, "Template App");
    }

    [Fact]
    public void DotnetNewBundleTemplate_FromFeed_RegeneratesGuids_AndSelfExtracts()
    {
        var feed = FindOnboardingFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);

        var hive = InstallTemplatesIntoIsolatedHive(feed);
        var projectDir = InstantiateTemplate(hive, "falkforge-bundle", "TplBundleApp", "Template Suite");

        // The template's source GUIDs must have been regenerated for this instantiation —
        // otherwise every project created from the template would upgrade-collide.
        var program = File.ReadAllText(Path.Combine(projectDir, "Program.cs"));
        var guids = Regex.Matches(program, """new Guid\("([0-9a-fA-F-]{36})"\)""")
            .Select(m => Guid.Parse(m.Groups[1].Value))
            .ToList();
        Assert.True(guids.Count >= 2, "instantiated bundle program must carry BundleId and UpgradeCode");
        Assert.DoesNotContain(Guid.Parse("0C55B22A-3B23-45A4-A3F1-6A1E2F0D5B01"), guids);
        Assert.DoesNotContain(Guid.Parse("5D8E3A9C-7F14-4B06-9C2D-8E4A1B6F7C02"), guids);

        var (_, outDir) = BuildAndRunInstallerProject(feed, projectDir, "TplBundleApp.exe");

        var bundlePath = Assert.Single(Directory.GetFiles(outDir, "*.exe"));
        var msiPath = Assert.Single(Directory.GetFiles(outDir, "*.msi"));
        AssertRunnableSelfExtractingBundle(bundlePath, msiPath);
    }
}
