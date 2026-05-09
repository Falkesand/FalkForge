using System.Text.Json;
using FalkForge.Engine.Download;
using Xunit;

namespace FalkForge.Engine.Tests.Download;

/// <summary>
/// Regression lockdown tests for UpdateFeedJsonContext.
/// A test failure here means a new property was added to UpdateFeed or UpdateFeedEntry
/// that either (a) carries a non-serializable system type, or (b) matches a sensitive-name
/// pattern without [JsonIgnore].
/// Fix by adding [JsonIgnore] to the offending property before merging.
/// </summary>
public sealed class UpdateFeedJsonContextRegressionTests
{
    private static readonly string[] SensitiveFragments = ["password", "secret", "token", "apikey", "passphrase", "pin", "credential"];

    [Fact]
    public void UpdateFeed_RoundTrip_ProducesExpectedJson()
    {
        var feed = BuildFullFeed();

        var json = JsonSerializer.Serialize(feed, UpdateFeedJsonContext.Default.UpdateFeed);

        Assert.False(string.IsNullOrWhiteSpace(json));

        var parsed = JsonSerializer.Deserialize(json, UpdateFeedJsonContext.Default.UpdateFeed);
        Assert.NotNull(parsed);
        Assert.Equal(feed.BundleId, parsed.BundleId);
        Assert.Equal(2, parsed.Entries.Length);
    }

    [Fact]
    public void UpdateFeed_Json_ContainsNoForbiddenSystemTypes()
    {
        var json = JsonSerializer.Serialize(BuildFullFeed(), UpdateFeedJsonContext.Default.UpdateFeed);

        Assert.DoesNotContain("System.IntPtr", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Delegate", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Action", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Func", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateFeed_Json_ContainsNoSensitiveKeys()
    {
        var json = JsonSerializer.Serialize(BuildFullFeed(), UpdateFeedJsonContext.Default.UpdateFeed);

        using var doc = JsonDocument.Parse(json);
        AssertNoSensitiveKeys(doc.RootElement, "$");
    }

    [Fact]
    public void UpdateFeedEntry_RoundTrip_AllFields()
    {
        var feed = BuildFullFeed();
        var json = JsonSerializer.Serialize(feed, UpdateFeedJsonContext.Default.UpdateFeed);
        var parsed = JsonSerializer.Deserialize(json, UpdateFeedJsonContext.Default.UpdateFeed);

        Assert.NotNull(parsed);
        var entry = parsed.Entries[0];
        Assert.Equal("2.0.0", entry.Version);
        Assert.Equal("https://marker.example.com/v2.msi", entry.Url);
        Assert.Equal("AABBCC", entry.Sha256);
        Assert.Equal(123456789L, entry.Size);
        Assert.Equal("MARKER_NOTES", entry.ReleaseNotes);
        Assert.Equal("2025-01-01", entry.Published);
        Assert.Equal("1.0.0", entry.MinVersion);
        Assert.Equal("https://marker.example.com/v2.delta", entry.DeltaUrl);
        Assert.Equal("DDEEFF", entry.DeltaSha256);
        Assert.Equal(56789L, entry.DeltaSize);
    }

    private static UpdateFeed BuildFullFeed() => new()
    {
        BundleId = new Guid("33333333-3333-3333-3333-333333333333"),
        Entries =
        [
            new UpdateFeedEntry
            {
                Version = "2.0.0",
                Url = "https://marker.example.com/v2.msi",
                Sha256 = "AABBCC",
                Size = 123456789L,
                ReleaseNotes = "MARKER_NOTES",
                Published = "2025-01-01",
                MinVersion = "1.0.0",
                DeltaUrl = "https://marker.example.com/v2.delta",
                DeltaSha256 = "DDEEFF",
                DeltaSize = 56789L
            },
            new UpdateFeedEntry
            {
                Version = "1.5.0",
                Url = "https://marker.example.com/v1.5.msi",
                Sha256 = "112233"
            }
        ]
    };

    private static void AssertNoSensitiveKeys(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    foreach (var fragment in SensitiveFragments)
                    {
                        Assert.False(
                            prop.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                            $"Sensitive key '{prop.Name}' found at {path}.{prop.Name}. Add [JsonIgnore] or justify.");
                    }
                    AssertNoSensitiveKeys(prop.Value, $"{path}.{prop.Name}");
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
