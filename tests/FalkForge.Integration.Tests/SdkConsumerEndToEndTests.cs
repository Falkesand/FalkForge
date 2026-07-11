using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Proves the LAST previously-unverified consumer path end to end: a code-first project whose
/// first line is <c>&lt;Project Sdk="FalkForge.Sdk/x.y.z"&gt;</c> (the MSBuild project-SDK
/// attribute form — NOT <c>Microsoft.NET.Sdk</c> plus a <c>PackageReference</c>) restores the
/// SDK package from a NuGet feed, gets the engine runtime implicitly, builds, and produces a
/// RUNNABLE self-extracting bundle.
/// <para>What this genuinely pins (previously unproven):</para>
/// <list type="number">
///   <item><description>MSBuild's NuGet-based project-SDK resolver fetches
///   <c>FalkForge.Sdk</c> from the feed configured in the consumer's <c>nuget.config</c>
///   (package source mapping pins every FalkForge* package to the local feed).</description></item>
///   <item><description>The SDK chains <c>Microsoft.NET.Sdk</c>: the consumer project compiles
///   like a normal C# project. Without the chain the build is a silent no-op — "Build
///   succeeded" with no restore, no compile, and no output.</description></item>
///   <item><description>The SDK's implicit <c>FalkForge.Engine.Runtime.win-x64</c> reference:
///   the consumer references only granular authoring packages (Core + both compilers), none of
///   which depend on the engine runtime — so the engine in the build output can ONLY have
///   arrived through the SDK's implicit reference pinned by the packed
///   <c>FalkForgeVersion.props</c>.</description></item>
///   <item><description>The produced bundle is runnable: its PE front is the real NativeAOT
///   engine and it self-extracts the chained MSI back out byte-for-byte.</description></item>
/// </list>
/// <para>
/// NuGet isolation mirrors <see cref="OnboardingEndToEndTests"/>: a private
/// <c>NUGET_PACKAGES</c> cache (also used by the project-SDK resolver) and a config whose
/// FalkForge packages can only come from the local feed, with nuget.org available for
/// third-party transitive dependencies. Gated on the feed produced by <c>scripts/pack.ps1</c>
/// with an explicit skip (never silent): packing requires the multi-minute NativeAOT engine
/// publish.
/// </para>
/// </summary>
public sealed class SdkConsumerEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public SdkConsumerEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkSdkE2E_{Guid.NewGuid():N}");
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
    /// The local feed and the single-source version of the packed <c>FalkForge.Sdk</c>, or null
    /// (→ explicit skip) unless the feed carries every package this consumer path needs: the
    /// MSBuild SDK, the engine runtime its props implicitly reference, and the granular
    /// authoring packages the consumer project references directly.
    /// </summary>
    private static (string Feed, string SdkVersion)? FindSdkFeed()
    {
        var root = FindRepoRoot();
        if (root is null)
            return null;

        var feed = Path.Combine(root, "artifacts", "nuget");
        if (!Directory.Exists(feed))
            return null;

        var sdkNupkg = Directory.GetFiles(feed, "FalkForge.Sdk.*.nupkg").SingleOrDefault();
        if (sdkNupkg is null)
            return null;

        var version = Regex.Match(Path.GetFileName(sdkNupkg),
            @"^FalkForge\.Sdk\.(\d.+)\.nupkg$").Groups[1].Value;
        if (version.Length == 0)
            return null;

        var required = new[]
        {
            "FalkForge.Engine.Runtime.win-x64",
            "FalkForge.Core",
            "FalkForge.Compiler.Msi",
            "FalkForge.Compiler.Bundle"
        };
        return required.All(id => Directory.GetFiles(feed, id + ".*.nupkg")
                .Any(f => char.IsAsciiDigit(Path.GetFileName(f)[(id.Length + 1)..][0])))
            ? (feed, version)
            : null;
    }

    private const string FeedSkipReason =
        "Local NuGet feed with FalkForge.Sdk, FalkForge.Engine.Runtime.win-x64, and the granular " +
        "authoring packages not found at artifacts/nuget — run scripts/pack.ps1 first. This gate " +
        "exists because packing requires the multi-minute NativeAOT engine publish.";

    // ---- plumbing ----

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

    private static void AssertRealEngineBinary(string path, string what)
    {
        Assert.True(File.Exists(path), $"expected {what} at {path}");

        var info = new FileInfo(path);
        Assert.True(info.Length > 1024 * 1024,
            $"{what} is {info.Length:N0} bytes — far too small to be the NativeAOT binary. " +
            "A placeholder must never ship.");

        using var stream = File.OpenRead(path);
        var prefix = new byte[2];
        stream.ReadExactly(prefix);
        Assert.Equal((byte)'M', prefix[0]);
        Assert.Equal((byte)'Z', prefix[1]);
    }

    [Fact]
    public void SdkAttributeConsumer_RestoresSdkAndEngineFromFeed_BuildsRunnableBundle()
    {
        var feed = FindSdkFeed();
        Assert.SkipUnless(feed is not null, FeedSkipReason);
        var (feedPath, sdkVersion) = feed.Value;

        // ---- consumer project: first line is the SDK-attribute form ----
        var projectDir = Path.Combine(_tempDir, "consumer");
        Directory.CreateDirectory(Path.Combine(projectDir, "payload"));

        // Source mapping pins every FalkForge* package — including the project SDK itself,
        // which MSBuild's NuGet-based SDK resolver fetches through this same config — to the
        // local feed. nuget.org stays available for third-party transitive dependencies
        // (e.g. Octopus.Octodiff via FalkForge.Compiler.Bundle).
        File.WriteAllText(Path.Combine(projectDir, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="falkforge-local" value="{feedPath}" />
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

        // Granular authoring packages ONLY — none of them depends on the engine runtime
        // (only the FalkForge meta-package does), so the engine restoring at all proves the
        // SDK's implicit FalkForge.Engine.Runtime.win-x64 reference. FalkOutputType=None:
        // the installer program is run manually with -o below, mirroring the other
        // onboarding proofs (and forge init / the templates' `dotnet run` workflow).
        File.WriteAllText(Path.Combine(projectDir, "SdkConsumer.csproj"), $"""
            <Project Sdk="FalkForge.Sdk/{sdkVersion}">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0-windows</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <FalkOutputType>None</FalkOutputType>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="FalkForge.Core" Version="{sdkVersion}" />
                <PackageReference Include="FalkForge.Compiler.Msi" Version="{sdkVersion}" />
                <PackageReference Include="FalkForge.Compiler.Bundle" Version="{sdkVersion}" />
              </ItemGroup>
              <ItemGroup>
                <None Include="payload/**" CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(projectDir, "payload", "readme.txt"),
            "SDK-attribute consumer payload.");

        // Minimal fluent bundle program, matching the falkforge-bundle template scaffold.
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), """
            using FalkForge;
            using FalkForge.Builders;
            using FalkForge.Compiler.Bundle.Builders;
            using FalkForge.Compiler.Bundle.Compilation;
            using FalkForge.Compiler.Msi;
            using FalkForge.Models;

            return Installer.BuildBundle(args, outputPath =>
            {
                var package = new PackageBuilder();
                package.Name = "SdkConsumerApp";
                package.Manufacturer = "Contoso";
                package.Version = new Version(1, 0, 0);

                package.UseDialogSet(MsiDialogSet.Minimal);

                package.Files(files => files
                    .FromDirectory("payload")
                    .To(KnownFolder.ProgramFiles / "Contoso" / "SdkConsumerApp"));

                var msi = new MsiCompiler().Compile(package.Build(), outputPath);
                if (msi.IsFailure)
                    return msi;

                var bundle = new BundleBuilder()
                    .Name("SdkConsumerApp")
                    .Manufacturer("Contoso")
                    .Version("1.0.0")
                    .BundleId(new Guid("7B7C3F62-1A2E-4E43-9A65-0D3B7A1C9F11"))
                    .UpgradeCode(new Guid("A94D1D8B-6C0F-4B7E-8E2A-51F0C2D4B322"))
                    .Scope(InstallScope.PerMachine)
                    .UseBuiltInUI(themeColor: "#0078D4")
                    .Chain(chain => chain
                        .MsiPackage(msi.Value, p => p
                            .Id("MainMsi")
                            .DisplayName("SdkConsumerApp")
                            .Vital(true)))
                    .Build();

                return new BundleCompiler().Compile(bundle, outputPath);
            });
            """);

        // ---- restore + build with fully isolated NuGet state ----
        var nugetCache = Path.Combine(_tempDir, "nuget-cache");
        var environment = new Dictionary<string, string>
        {
            // The project-SDK resolver and package restore share this private cache: the SDK
            // landing here proves it was fetched through the consumer's nuget.config, not
            // found in an ambient machine cache.
            ["NUGET_PACKAGES"] = nugetCache,
            // The engine must resolve from the restored package's copy in the build output,
            // never from an ambient override or the developer machine's publish tree.
            ["FALKFORGE_ENGINE_STUB"] = ""
        };

        var (buildExit, buildOut, buildErr) = RunProcess("dotnet",
            ["build", projectDir], environment, workingDirectory: projectDir);
        Assert.True(buildExit == 0,
            $"consumer build failed (exit {buildExit}). stdout: {buildOut} stderr: {buildErr}");

        // The SDK package was genuinely restored from the feed into the isolated cache.
        var sdkCacheDir = Path.Combine(nugetCache, "falkforge.sdk", sdkVersion);
        Assert.True(Directory.Exists(sdkCacheDir),
            $"FalkForge.Sdk {sdkVersion} must have been restored into the isolated cache at {sdkCacheDir}");

        // The implicit engine-runtime reference restored: it appears in the resolved package
        // graph even though neither the consumer project nor any referenced package asks for it.
        var assetsFile = Path.Combine(projectDir, "obj", "project.assets.json");
        Assert.True(File.Exists(assetsFile),
            "restore must have produced obj/project.assets.json — its absence means the SDK " +
            "did not chain Microsoft.NET.Sdk and the build was a silent no-op");
        Assert.Contains("FalkForge.Engine.Runtime.win-x64", File.ReadAllText(assetsFile),
            StringComparison.OrdinalIgnoreCase);

        // The engine landed in the consumer's build output where the bundle compiler's
        // beside-host probe resolves it.
        var binDir = Path.Combine(projectDir, "bin", "Debug", "net10.0-windows");
        AssertRealEngineBinary(Path.Combine(binDir, "engine", "FalkForge.Engine.exe"),
            "engine copied to consumer output");
        AssertRealEngineBinary(Path.Combine(binDir, "engine", "FalkForge.Engine.Elevation.exe"),
            "elevation companion copied to consumer output");

        // ---- run the built installer program: the bundle output must be RUNNABLE ----
        var exePath = Path.Combine(binDir, "SdkConsumer.exe");
        Assert.True(File.Exists(exePath), $"expected built installer program at {exePath}");

        var outDir = Path.Combine(projectDir, "installer-out");
        var (runExit, runOut, runErr) = RunProcess(exePath, ["-o", outDir],
            environment, workingDirectory: projectDir, timeoutMinutes: 5);
        Assert.True(runExit == 0,
            $"installer program failed (exit {runExit}). stdout: {runOut} stderr: {runErr}");

        var bundlePath = Assert.Single(Directory.GetFiles(outDir, "*.exe"));
        var msiPath = Assert.Single(Directory.GetFiles(outDir, "*.msi"));

        // The bundle's PE front is the real engine: MZ header and megabytes of NativeAOT
        // binary before the payload even starts.
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

        var extractDir = Path.Combine(_tempDir, "extracted");
        var (extractExit, _, extractErr) = RunProcess(bundlePath, ["--extract", extractDir], timeoutMinutes: 5);
        Assert.True(extractExit == 0, $"--extract failed (exit {extractExit}). stderr: {extractErr}");

        var extractedMsi = Path.Combine(extractDir, "MainMsi", "MainMsi.dat");
        Assert.True(File.Exists(extractedMsi), $"expected extracted MSI at {extractedMsi}");
        Assert.Equal(File.ReadAllBytes(msiPath), File.ReadAllBytes(extractedMsi));
    }
}
