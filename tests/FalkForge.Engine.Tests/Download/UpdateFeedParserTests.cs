using FalkForge.Engine.Download;
using Xunit;

namespace FalkForge.Engine.Tests.Download;

public sealed class UpdateFeedParserTests
{
    private static readonly Guid TestBundleId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    private static byte[] MakeFeedJson(
        Guid bundleId,
        params (string version, string url, string sha256)[] entries)
    {
        var entryJsons = entries.Select(e => $$"""
            {
                "version": "{{e.version}}",
                "url": "{{e.url}}",
                "sha256": "{{e.sha256}}"
            }
            """);

        var json = $$"""
        {
            "bundleId": "{{bundleId}}",
            "entries": [
                {{string.Join(",\n", entryJsons)}}
            ]
        }
        """;
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    private static byte[] MakeFeedJsonWithExtras(
        Guid bundleId,
        params (string version, string url, string sha256, long? size, string? releaseNotes, string? minVersion)[] entries)
    {
        var entryJsons = entries.Select(e =>
        {
            var parts = new List<string>
            {
                $"""  "version": "{e.version}" """.Trim(),
                $"""  "url": "{e.url}" """.Trim(),
                $"""  "sha256": "{e.sha256}" """.Trim()
            };

            if (e.size.HasValue)
                parts.Add($"""  "size": {e.size.Value} """.Trim());
            if (e.releaseNotes is not null)
                parts.Add($"""  "releaseNotes": "{e.releaseNotes}" """.Trim());
            if (e.minVersion is not null)
                parts.Add($"""  "minVersion": "{e.minVersion}" """.Trim());

            return "{ " + string.Join(", ", parts) + " }";
        });

        var json = $$"""
        {
            "bundleId": "{{bundleId}}",
            "entries": [
                {{string.Join(",\n", entryJsons)}}
            ]
        }
        """;
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    [Fact]
    public void Parse_ValidFeed_ReturnsNewestUpdate()
    {
        var json = MakeFeedJson(TestBundleId,
            ("1.0.0", "https://example.com/v1.exe", "aaa"),
            ("2.0.0", "https://example.com/v2.exe", "bbb"),
            ("3.0.0", "https://example.com/v3.exe", "ccc"));

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.0.0");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Update);
        Assert.Equal("3.0.0", result.Value.Update!.Version);
        Assert.Equal("https://example.com/v3.exe", result.Value.Update.DownloadUrl);
        Assert.Equal("ccc", result.Value.Update.Sha256);
    }

    [Fact]
    public void Parse_NoNewerVersion_ReturnsNull()
    {
        var json = MakeFeedJson(TestBundleId,
            ("1.0.0", "https://example.com/v1.exe", "aaa"),
            ("0.9.0", "https://example.com/v09.exe", "bbb"));

        var result = UpdateFeedParser.Parse(json, TestBundleId, "2.0.0");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Update);
    }

    [Fact]
    public void Parse_BundleIdMismatch_ReturnsUPD003()
    {
        var wrongId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var json = MakeFeedJson(wrongId,
            ("2.0.0", "https://example.com/v2.exe", "aaa"));

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.0.0");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD003", result.Error.Message);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsUPD002()
    {
        var json = "{ this is not valid json }"u8.ToArray();

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.0.0");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD002", result.Error.Message);
    }

    [Fact]
    public void Parse_MinVersionFilter_ExcludesIneligible()
    {
        var json = MakeFeedJsonWithExtras(TestBundleId,
            ("3.0.0", "https://example.com/v3.exe", "aaa", null, null, "2.0.0"));

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.0.0");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Update);
    }

    [Fact]
    public void Parse_MinVersionFilter_IncludesEligible()
    {
        var json = MakeFeedJsonWithExtras(TestBundleId,
            ("3.0.0", "https://example.com/v3.exe", "aaa", null, null, "1.0.0"));

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.5.0");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Update);
        Assert.Equal("3.0.0", result.Value.Update!.Version);
    }

    [Fact]
    public void Parse_InvalidEntryVersion_Skipped()
    {
        var json = MakeFeedJson(TestBundleId,
            ("notaversion", "https://example.com/bad.exe", "aaa"),
            ("2.0.0", "https://example.com/v2.exe", "bbb"));

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.0.0");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Update);
        Assert.Equal("2.0.0", result.Value.Update!.Version);
    }

    [Fact]
    public void Parse_EmptyEntries_ReturnsNull()
    {
        var json = System.Text.Encoding.UTF8.GetBytes($$"""
        {
            "bundleId": "{{TestBundleId}}",
            "entries": []
        }
        """);

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.0.0");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Update);
    }

    [Fact]
    public void Parse_NullFeed_ReturnsUPD002()
    {
        var json = "null"u8.ToArray();

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.0.0");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.EngineError, result.Error.Kind);
        Assert.Contains("UPD002", result.Error.Message);
    }

    [Fact]
    public void Parse_MinVersionExactlyEqual_IncludesEntry()
    {
        var json = MakeFeedJsonWithExtras(TestBundleId,
            ("2.0.0", "https://example.com/v2.exe", "AA", null, null, "1.5.0"));

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.5.0");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Update);
        Assert.Equal("2.0.0", result.Value.Update!.Version);
    }

    [Fact]
    public void Parse_CorrectFields_Returned()
    {
        var json = MakeFeedJsonWithExtras(TestBundleId,
            ("2.0.0", "https://cdn.example.com/v2/bundle.exe", "abc123def456", 15_000_000L, "Fixed critical bug", null));

        var result = UpdateFeedParser.Parse(json, TestBundleId, "1.0.0");

        Assert.True(result.IsSuccess);
        var update = result.Value.Update;
        Assert.NotNull(update);
        Assert.Equal("2.0.0", update!.Version);
        Assert.Equal("https://cdn.example.com/v2/bundle.exe", update.DownloadUrl);
        Assert.Equal("abc123def456", update.Sha256);
        Assert.Equal(15_000_000L, update.Size);
        Assert.Equal("Fixed critical bug", update.ReleaseNotes);
    }
}
