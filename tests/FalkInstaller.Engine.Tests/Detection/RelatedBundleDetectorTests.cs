namespace FalkInstaller.Engine.Tests.Detection;

using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Tests.Mocks;
using Xunit;

public sealed class RelatedBundleDetectorTests
{
    private const string HklmUninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string HklmWow64UninstallPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string HkcuUninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly RelatedBundleDetector _detector = new();

    [Fact]
    public void Detect_NoRelatedBundles_ReturnsEmptyList()
    {
        var registry = new MockRegistry();
        var relatedBundles = Array.Empty<RelatedBundleEntry>();

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Detect_MatchingUpgradeCodeInHklm_ReturnsRelatedBundle()
    {
        var upgradeCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var subKeyName = "{11111111-2222-3333-4444-555555555555}";
        var entryPath = $@"{HklmUninstallPath}\{subKeyName}";

        var registry = new MockRegistry()
            .AddKey("HKLM", entryPath)
            .SetStringValue("HKLM", entryPath, "BundleUpgradeCode", upgradeCode)
            .SetStringValue("HKLM", entryPath, "DisplayVersion", "1.0.0")
            .SetStringValue("HKLM", entryPath, "DisplayName", "OldApp");

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = upgradeCode,
                Relation = RelatedBundleRelation.Upgrade
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(upgradeCode, result.Value[0].BundleId);
        Assert.Equal("1.0.0", result.Value[0].InstalledVersion);
        Assert.Equal(RelatedBundleRelation.Upgrade, result.Value[0].Relation);
        Assert.NotNull(result.Value[0].RegistryKeyPath);
    }

    [Fact]
    public void Detect_MatchingUpgradeCodeInHkcu_ReturnsRelatedBundle()
    {
        var upgradeCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var subKeyName = "{11111111-2222-3333-4444-555555555555}";
        var entryPath = $@"{HkcuUninstallPath}\{subKeyName}";

        var registry = new MockRegistry()
            .AddKey("HKCU", entryPath)
            .SetStringValue("HKCU", entryPath, "BundleUpgradeCode", upgradeCode)
            .SetStringValue("HKCU", entryPath, "DisplayVersion", "2.5.0");

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = upgradeCode,
                Relation = RelatedBundleRelation.Detect
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(upgradeCode, result.Value[0].BundleId);
        Assert.Equal("2.5.0", result.Value[0].InstalledVersion);
        Assert.Equal(RelatedBundleRelation.Detect, result.Value[0].Relation);
    }

    [Fact]
    public void Detect_MatchingUpgradeCodeInWow64_ReturnsRelatedBundle()
    {
        var upgradeCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var subKeyName = "{11111111-2222-3333-4444-555555555555}";
        var entryPath = $@"{HklmWow64UninstallPath}\{subKeyName}";

        var registry = new MockRegistry()
            .AddKey("HKLM", entryPath)
            .SetStringValue("HKLM", entryPath, "BundleUpgradeCode", upgradeCode)
            .SetStringValue("HKLM", entryPath, "DisplayVersion", "1.2.3");

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = upgradeCode,
                Relation = RelatedBundleRelation.Addon
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("1.2.3", result.Value[0].InstalledVersion);
        Assert.Equal(RelatedBundleRelation.Addon, result.Value[0].Relation);
    }

    [Fact]
    public void Detect_NoMatchingUpgradeCode_ReturnsEmptyList()
    {
        var subKeyName = "{11111111-2222-3333-4444-555555555555}";
        var entryPath = $@"{HklmUninstallPath}\{subKeyName}";

        var registry = new MockRegistry()
            .AddKey("HKLM", entryPath)
            .SetStringValue("HKLM", entryPath, "BundleUpgradeCode", "{DIFFERENT-CODE-0000-0000-000000000000}")
            .SetStringValue("HKLM", entryPath, "DisplayVersion", "1.0.0");

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
                Relation = RelatedBundleRelation.Upgrade
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Detect_MultipleMatches_ReturnsAll()
    {
        var upgradeCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var subKey1 = "{11111111-1111-1111-1111-111111111111}";
        var subKey2 = "{22222222-2222-2222-2222-222222222222}";
        var entry1 = $@"{HklmUninstallPath}\{subKey1}";
        var entry2 = $@"{HkcuUninstallPath}\{subKey2}";

        var registry = new MockRegistry()
            .AddKey("HKLM", entry1)
            .SetStringValue("HKLM", entry1, "BundleUpgradeCode", upgradeCode)
            .SetStringValue("HKLM", entry1, "DisplayVersion", "1.0.0")
            .AddKey("HKCU", entry2)
            .SetStringValue("HKCU", entry2, "BundleUpgradeCode", upgradeCode)
            .SetStringValue("HKCU", entry2, "DisplayVersion", "2.0.0");

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = upgradeCode,
                Relation = RelatedBundleRelation.Upgrade
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public void Detect_MissingDisplayVersion_DefaultsToZero()
    {
        var upgradeCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var subKeyName = "{11111111-2222-3333-4444-555555555555}";
        var entryPath = $@"{HklmUninstallPath}\{subKeyName}";

        var registry = new MockRegistry()
            .AddKey("HKLM", entryPath)
            .SetStringValue("HKLM", entryPath, "BundleUpgradeCode", upgradeCode);

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = upgradeCode,
                Relation = RelatedBundleRelation.Upgrade
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("0.0.0", result.Value[0].InstalledVersion);
    }

    [Fact]
    public void Detect_RelationTypePreservedFromModel()
    {
        var upgradeCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var subKeyName = "{11111111-2222-3333-4444-555555555555}";
        var entryPath = $@"{HklmUninstallPath}\{subKeyName}";

        var registry = new MockRegistry()
            .AddKey("HKLM", entryPath)
            .SetStringValue("HKLM", entryPath, "BundleUpgradeCode", upgradeCode)
            .SetStringValue("HKLM", entryPath, "DisplayVersion", "1.0.0");

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = upgradeCode,
                Relation = RelatedBundleRelation.Patch
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(RelatedBundleRelation.Patch, result.Value[0].Relation);
    }

    [Fact]
    public void Detect_EntryWithoutBundleUpgradeCode_IsSkipped()
    {
        var subKeyName = "{11111111-2222-3333-4444-555555555555}";
        var entryPath = $@"{HklmUninstallPath}\{subKeyName}";

        var registry = new MockRegistry()
            .AddKey("HKLM", entryPath)
            .SetStringValue("HKLM", entryPath, "DisplayVersion", "1.0.0")
            .SetStringValue("HKLM", entryPath, "DisplayName", "Some App");

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
                Relation = RelatedBundleRelation.Upgrade
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Detect_RegistryKeyPathIncludesRootAndSubKey()
    {
        var upgradeCode = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";
        var subKeyName = "{11111111-2222-3333-4444-555555555555}";
        var entryPath = $@"{HklmUninstallPath}\{subKeyName}";

        var registry = new MockRegistry()
            .AddKey("HKLM", entryPath)
            .SetStringValue("HKLM", entryPath, "BundleUpgradeCode", upgradeCode)
            .SetStringValue("HKLM", entryPath, "DisplayVersion", "1.0.0");

        var relatedBundles = new[]
        {
            new RelatedBundleEntry
            {
                BundleId = upgradeCode,
                Relation = RelatedBundleRelation.Upgrade
            }
        };

        var result = _detector.Detect(relatedBundles, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.StartsWith("HKLM\\", result.Value[0].RegistryKeyPath);
        Assert.Contains(subKeyName, result.Value[0].RegistryKeyPath);
    }
}
