using System.Text.Json;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Ui;
using Xunit;

namespace FalkForge.Ui.Tests;

/// <summary>
/// Regression lockdown tests for the Ui.ManifestJsonContext.
/// A test failure here means a new property was added to InstallerManifest or a related
/// manifest type that either (a) carries a non-serializable system type, or (b) matches
/// a sensitive-name pattern without [JsonIgnore].
/// Fix by adding [JsonIgnore] to the offending property before merging.
/// </summary>
public sealed class ManifestJsonContextRegressionTests
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

        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallerManifest);

        Assert.False(string.IsNullOrWhiteSpace(json));

        var parsed = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest);
        Assert.NotNull(parsed);
        Assert.Equal("UI_MARKER_NAME", parsed.Name);
        Assert.Equal("UI_MARKER_MFR", parsed.Manufacturer);
        Assert.Equal("5.4.3.2", parsed.Version);
        Assert.Single(parsed.Packages);
    }

    [Fact]
    public void InstallerManifest_Json_ContainsNoForbiddenSystemTypes()
    {
        var json = JsonSerializer.Serialize(BuildFullManifest(), ManifestJsonContext.Default.InstallerManifest);

        Assert.DoesNotContain("System.IntPtr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Delegate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Action", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Func", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerManifest_Json_ContainsNoUnwhitelistedSensitiveKeys()
    {
        var json = JsonSerializer.Serialize(BuildFullManifest(), ManifestJsonContext.Default.InstallerManifest);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    [Fact]
    public void InstallerManifest_ChainItems_RoundTrip()
    {
        var manifest = BuildFullManifest();
        var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.InstallerManifest);
        var parsed = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Chain.Length);
    }

    private static InstallerManifest BuildFullManifest() => new()
    {
        Name = "UI_MARKER_NAME",
        Manufacturer = "UI_MARKER_MFR",
        Version = "5.4.3.2",
        BundleId = new Guid("66666666-6666-6666-6666-666666666666"),
        UpgradeCode = new Guid("77777777-7777-7777-7777-777777777777"),
        Scope = InstallScope.PerMachine,
        Packages =
        [
            new PackageInfo
            {
                Id = "UI_PKG",
                Type = PackageType.MsiPackage,
                DisplayName = "UI_PKG_DISPLAY",
                SourcePath = @"C:\ui\marker.msi",
                Sha256Hash = "0011223344556677889900AABBCCDDEEFF001122334455667788990011223344"
            }
        ],
        Chain =
        [
            new PackageManifestChainItem(new PackageInfo
            {
                Id = "UI_CHAIN_PKG",
                Type = PackageType.MsiPackage,
                DisplayName = "UI_CHAIN_DISPLAY",
                SourcePath = @"C:\ui\chain.msi",
                Sha256Hash = "AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899"
            }),
            new RollbackBoundaryManifestChainItem(new RollbackBoundaryInfo { Id = "UI_RBB_ID" })
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
