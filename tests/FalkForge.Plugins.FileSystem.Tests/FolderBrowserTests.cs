namespace FalkForge.Plugins.FileSystem.Tests;

using FalkForge.Diagnostics;
using FalkForge.Testing;
using Xunit;

public sealed class FolderBrowserTests
{
    [Fact]
    public void BrowseForFolder_returns_selected_path_and_logs_info()
    {
        var logger = new ListLogger();
        var browser = new FolderBrowser((_, _) => @"C:\Selected", logger);

        var result = browser.BrowseForFolder();

        Assert.Equal(@"C:\Selected", result);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Info && e.Message.Contains("Opening folder browse dialog"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Info && e.Message.Contains("Folder selected"));
    }

    [Fact]
    public void BrowseForFolder_returns_null_and_logs_debug_when_canceled()
    {
        var logger = new ListLogger();
        var browser = new FolderBrowser((_, _) => null, logger);

        var result = browser.BrowseForFolder();

        Assert.Null(result);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("canceled"));
    }

    [Fact]
    public void BrowseForFolder_dialog_exception_logs_error_and_rethrows()
    {
        var logger = new ListLogger();
        var browser = new FolderBrowser((_, _) => throw new InvalidOperationException("dialog crashed"), logger);

        var ex = Assert.Throws<InvalidOperationException>(() => browser.BrowseForFolder());

        Assert.Equal("dialog crashed", ex.Message);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("PluginError", error.Properties!["code"]);
        Assert.Contains("dialog crashed", error.Properties["exception.message"]);
    }

    [Fact]
    public void BrowseForFolder_passes_initial_directory_and_description_to_dialog()
    {
        string? capturedDir = null;
        string? capturedDesc = null;
        var browser = new FolderBrowser((dir, desc) =>
        {
            capturedDir = dir;
            capturedDesc = desc;
            return null;
        });

        browser.BrowseForFolder(@"C:\Start", "Pick one");

        Assert.Equal(@"C:\Start", capturedDir);
        Assert.Equal("Pick one", capturedDesc);
    }
}
