namespace FalkForge.Engine.Tests.Bootstrap;

using FalkForge;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="PreUIPrerequisiteDetector"/>.
/// Phase 2 scope: detection only — no installation, no TaskDialog.
/// </summary>
public sealed class PreUIPrerequisiteDetectorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    // Mirrors BuiltInPrerequisites.DotNet10DesktopAsPreUI's actual emitted condition shape (see
    // BuiltInPrerequisitesTests in FalkForge.Compiler.Bundle.Tests for the production-method coverage
    // this project cannot reach directly — Engine.Tests has no reference to Compiler.Bundle).
    private static PreUIPackageInfo BuildDotNetDesktopPrereq(string id = "DotNet10Desktop") =>
        new()
        {
            Id = id,
            DisplayName = ".NET 10 Desktop Runtime (x64)",
            SourcePath = "dotnet-runtime-10.0-win-x64.exe",
            Sha256Hash = "AABBCC",
            Arguments = "/quiet /norestart",
            SearchConditions =
            [
                new SearchCondition
                {
                    Type = SearchConditionType.SharedFrameworkVersion,
                    Path = "Microsoft.WindowsDesktop.App",
                    Value = "10.0.0"
                }
            ]
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DetectsMissing_WhenSharedFrameworkDirectoryAbsent()
    {
        // Arrange — no DOTNET_ROOT, no sharedhost registry value, no shared-framework directory: the
        // resolved dotnet root cannot be determined at all, exactly like a machine with neither
        // environment variable nor registry override set.
        var registry = new MockRegistry();
        var fs = new MockFileSystemProvider();
        var detector = new PreUIPrerequisiteDetector(registry, fs);
        var prereq = BuildDotNetDesktopPrereq();

        // Act
        var missing = detector.FindMissing([prereq]);

        // Assert — prereq must appear in missing list because no matching version directory was found
        Assert.Contains(missing, p => p.Id == prereq.Id);
    }

    [Fact]
    public void DetectsInstalled_WhenSharedFrameworkDirectoryPresent()
    {
        // Arrange — the real on-disk shape: a version-named subdirectory under
        // <dotnet-root>\shared\Microsoft.WindowsDesktop.App, NOT a registry value. This is what
        // "matches what actually exists on disk" (see docs/release-notes/v0.5.0-beta.7.md) means —
        // the prior version of this test mocked a registry value name+data pair that no real machine
        // has ever produced.
        var registry = new MockRegistry();
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.10");
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\Program Files\dotnet");
        var detector = new PreUIPrerequisiteDetector(registry, fs, environment);
        var prereq = BuildDotNetDesktopPrereq();

        // Act
        var missing = detector.FindMissing([prereq]);

        // Assert — prereq must NOT appear in missing list
        Assert.DoesNotContain(missing, p => p.Id == prereq.Id);
    }

    [Fact]
    public void FindMissing_ReturnsEmpty_WhenNoDeclaredPackages()
    {
        var registry = new MockRegistry();
        var fs = new MockFileSystemProvider();
        var detector = new PreUIPrerequisiteDetector(registry, fs);

        var missing = detector.FindMissing([]);

        Assert.Empty(missing);
    }

    [Fact]
    public void FindMissing_TreatsNoConditionsAsInstalled()
    {
        // A prereq with zero SearchConditions is treated as ALREADY INSTALLED (safe default).
        // Phase 1 compiler validator (BDL026) ensures real bundles always have ≥1 condition;
        // this test guards the runtime against malformed manifests failing closed.
        var registry = new MockRegistry();
        var fs = new MockFileSystemProvider();
        var detector = new PreUIPrerequisiteDetector(registry, fs);

        var prereq = new PreUIPackageInfo
        {
            Id = "NoConditions",
            DisplayName = "No Conditions",
            SourcePath = "noop.exe",
            Sha256Hash = "AABBCC",
            Arguments = "/quiet",
            SearchConditions = []
        };

        var missing = detector.FindMissing([prereq]);

        // No conditions → already installed (pass-through). The phase-1 validator
        // (BDL026) prevents zero-condition prereqs from being compiled; this is a
        // defense-in-depth runtime guard that errs on the side of not blocking the UI.
        Assert.Empty(missing);
    }

    [Fact]
    public void FindMissing_AllConditionsMustPass_ForInstalled()
    {
        // Two conditions: shared-framework directory present BUT file condition fails.
        // Prereq must be reported as missing because not ALL conditions pass.
        var registry = new MockRegistry();
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.10");
            // no file added → FileExists will return false
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\Program Files\dotnet");
        var detector = new PreUIPrerequisiteDetector(registry, fs, environment);

        var prereq = new PreUIPackageInfo
        {
            Id = "MultiCondition",
            DisplayName = "Multi-condition prereq",
            SourcePath = "app.exe",
            Sha256Hash = "AABBCC",
            Arguments = "/quiet",
            SearchConditions =
            [
                new SearchCondition
                {
                    Type = SearchConditionType.SharedFrameworkVersion,
                    Path = "Microsoft.WindowsDesktop.App",
                    Value = "10.0.0"
                },
                new SearchCondition
                {
                    Type = SearchConditionType.FileExists,
                    Path = @"C:\Program Files\dotnet\dotnet.exe"
                }
            ]
        };

        var missing = detector.FindMissing([prereq]);

        Assert.Contains(missing, p => p.Id == prereq.Id);
    }
}
