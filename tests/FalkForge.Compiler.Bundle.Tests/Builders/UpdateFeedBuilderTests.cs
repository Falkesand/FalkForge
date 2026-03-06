using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class UpdateFeedBuilderTests
{
    [Fact]
    public void UpdateFeed_SetsConfigOnModel()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .UpdateFeed("https://updates.example.com/feed.json")
            .Build();

        Assert.NotNull(model.UpdateFeed);
        Assert.Equal("https://updates.example.com/feed.json", model.UpdateFeed.FeedUrl);
        Assert.Equal(UpdatePolicy.NotifyOnly, model.UpdateFeed.Policy);
    }

    [Fact]
    public void UpdateFeed_CustomPolicy_SetsPolicy()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .UpdateFeed("https://updates.example.com/feed.json", UpdatePolicy.AutoUpdate)
            .Build();

        Assert.NotNull(model.UpdateFeed);
        Assert.Equal(UpdatePolicy.AutoUpdate, model.UpdateFeed.Policy);
    }

    [Fact]
    public void UpdateFeed_NullUrl_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.UpdateFeed(null!));
    }

    [Fact]
    public void UpdateFeed_EmptyUrl_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentException>(() => builder.UpdateFeed(""));
    }

    [Fact]
    public void UpdateFeed_NotSet_ModelHasNull()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Build();

        Assert.Null(model.UpdateFeed);
    }

    [Fact]
    public void UpdateFeed_WhitespaceUrl_Throws()
    {
        var builder = new BundleBuilder();
        Assert.Throws<ArgumentException>(() => builder.UpdateFeed("   "));
    }

    [Fact]
    public void UpdateFeedConfig_NewProperties_HaveCorrectDefaults()
    {
        var config = new UpdateFeedConfig { FeedUrl = "https://example.com/feed.json" };

        Assert.True(config.ShowDownloadProgress);
        Assert.False(config.ShowDownloadErrors);
        Assert.False(config.PromptBeforeAutoUpdate);
    }
}
