namespace FalkForge.Engine.Tests.Bootstrap;

using FalkForge;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Engine.Protocol.Manifest;
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

    private static PreUIPackageInfo BuildRegistryPrereq(string id = "DotNet10Desktop") =>
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
                    Type = SearchConditionType.RegistryValue,
                    Path = @"HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App",
                    Value = "10.0.0",
                    Comparison = ">=:10.0.0"
                }
            ]
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DetectsMissing_WhenRegistryAbsent()
    {
        // Arrange — registry has no .NET 10 key at all
        var registry = new MockRegistry();
        var fs = new MockFileSystemProvider();
        var detector = new PreUIPrerequisiteDetector(registry, fs);
        var prereq = BuildRegistryPrereq();

        // Act
        var missing = detector.FindMissing([prereq]);

        // Assert — prereq must appear in missing list because registry key absent
        Assert.Contains(missing, p => p.Id == prereq.Id);
    }

    [Fact]
    public void DetectsInstalled_WhenRegistryPresent()
    {
        // Arrange — registry has matching version value
        var registry = new MockRegistry()
            .SetStringValue(
                RegistryRoot.LocalMachine,
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App",
                "10.0.0",
                "10.0.0");
        var fs = new MockFileSystemProvider();
        var detector = new PreUIPrerequisiteDetector(registry, fs);
        var prereq = BuildRegistryPrereq();

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
        // Two conditions: registry key present BUT file condition fails.
        // Prereq must be reported as missing because not ALL conditions pass.
        var registry = new MockRegistry()
            .SetStringValue(
                RegistryRoot.LocalMachine,
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App",
                "10.0.0",
                "10.0.0");
        var fs = new MockFileSystemProvider(); // no file added → FileExists will return false
        var detector = new PreUIPrerequisiteDetector(registry, fs);

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
                    Type = SearchConditionType.RegistryValue,
                    Path = @"HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App",
                    Value = "10.0.0",
                    Comparison = ">=:10.0.0"
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
