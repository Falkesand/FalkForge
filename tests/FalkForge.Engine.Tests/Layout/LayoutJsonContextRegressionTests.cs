using System.Text.Json;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Tests.Layout;

/// <summary>
/// Regression lockdown tests for LayoutJsonContext.
/// A test failure here means a new property was added to InstallerManifest or a related
/// manifest type that either (a) carries a non-serializable system type, or (b) matches
/// a sensitive-name pattern without [JsonIgnore].
/// Fix by adding [JsonIgnore] to the offending property before merging.
/// </summary>
public sealed class LayoutJsonContextRegressionTests
{
    private static readonly string[] SensitiveFragments = ["password", "secret", "token", "apikey", "passphrase", "pin", "credential"];

    // ManifestVariable.Secret is a boolean flag (not a secret value) — permitted.
    private static readonly HashSet<string> AllowedSensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "secret",
        "hidden"
    };

    [Fact]
    public void InstallerManifest_RoundTrip_ProducesExpectedJson()
    {
        var manifest = BuildFullManifest();

        var json = JsonSerializer.Serialize(manifest, LayoutJsonContext.Default.InstallerManifest);

        Assert.False(string.IsNullOrWhiteSpace(json));

        var parsed = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.InstallerManifest);
        Assert.NotNull(parsed);
        Assert.Equal("LAYOUT_MARKER_NAME", parsed.Name);
        Assert.Equal(InstallScope.PerUser, parsed.Scope);
        Assert.Single(parsed.Packages);
    }

    [Fact]
    public void InstallerManifest_Json_ContainsNoForbiddenSystemTypes()
    {
        var json = JsonSerializer.Serialize(BuildFullManifest(), LayoutJsonContext.Default.InstallerManifest);

        Assert.DoesNotContain("System.IntPtr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Delegate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Action", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Func", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerManifest_Json_ContainsNoUnwhitelistedSensitiveKeys()
    {
        var json = JsonSerializer.Serialize(BuildFullManifest(), LayoutJsonContext.Default.InstallerManifest);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    [Fact]
    public void InstallerManifest_DependencyRequirements_RoundTrip()
    {
        var manifest = BuildFullManifest();
        var json = JsonSerializer.Serialize(manifest, LayoutJsonContext.Default.InstallerManifest);
        var parsed = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.InstallerManifest);

        Assert.NotNull(parsed);
        Assert.Single(parsed.DependencyRequirements);
        Assert.Equal("LAYOUT_PROVIDER_KEY", parsed.DependencyRequirements[0].ProviderKey);
        Assert.Equal("1.0.0", parsed.DependencyRequirements[0].MinVersion);
    }

    private static InstallerManifest BuildFullManifest() => new()
    {
        Name = "LAYOUT_MARKER_NAME",
        Manufacturer = "LAYOUT_MARKER_MFR",
        Version = "3.2.1.0",
        BundleId = new Guid("44444444-4444-4444-4444-444444444444"),
        UpgradeCode = new Guid("55555555-5555-5555-5555-555555555555"),
        Scope = InstallScope.PerUser,
        Packages =
        [
            new PackageInfo
            {
                Id = "LAYOUT_PKG",
                Type = PackageType.MsiPackage,
                DisplayName = "LAYOUT_PKG_DISPLAY",
                SourcePath = @"C:\layout\marker.msi",
                Sha256Hash = "FFEEDDCCBBAA99887766554433221100FFEEDDCCBBAA99887766554433221100"
            }
        ],
        DependencyRequirements =
        [
            new ManifestDependencyRequirement(
                ProviderKey: "LAYOUT_PROVIDER_KEY",
                MinVersion: "1.0.0",
                MaxVersion: "2.0.0",
                MinInclusive: true,
                MaxInclusive: false)
        ]
    };

    private void AssertNoSensitiveKeys(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = prop.Name;
                    if (!AllowedSensitiveKeys.Contains(key))
                    {
                        foreach (var fragment in SensitiveFragments)
                        {
                            Assert.False(
                                key.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                                $"Sensitive key '{key}' found at {path}.{key}. Add [JsonIgnore] or add to AllowedSensitiveKeys with justification.");
                        }
                    }
                    AssertNoSensitiveKeys(prop.Value, $"{path}.{key}");
                }
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in element.EnumerateArray())
                    AssertNoSensitiveKeys(item, $"{path}[{i++}]");
                break;
        }
    }
}
