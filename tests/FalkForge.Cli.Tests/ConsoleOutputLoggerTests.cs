using FalkForge.Diagnostics;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Unit tests for <see cref="ConsoleOutputLogger"/>, the <see cref="IFalkLogger"/> adapter
/// that routes Compiler.Msi's structured log entries through the CLI's <see cref="IConsoleOutput"/>.
/// </summary>
public sealed class ConsoleOutputLoggerTests
{
    [Fact]
    public void Constructor_NotVerbose_MinimumLevelIsInfo()
    {
        var logger = new ConsoleOutputLogger(new TestConsoleOutput(), verbose: false);

        Assert.Equal(LogLevel.Info, logger.MinimumLevel);
    }

    [Fact]
    public void Constructor_Verbose_MinimumLevelIsDebug()
    {
        var logger = new ConsoleOutputLogger(new TestConsoleOutput(), verbose: true);

        Assert.Equal(LogLevel.Debug, logger.MinimumLevel);
    }

    [Fact]
    public void Error_RoutesToWriteError()
    {
        var console = new TestConsoleOutput();
        var logger = new ConsoleOutputLogger(console, verbose: false);

        logger.Error("MsiAuthoring", "boom");

        var entry = Assert.Single(console.Errors);
        Assert.Contains("MsiAuthoring", entry);
        Assert.Contains("boom", entry);
        Assert.Empty(console.MarkupLines);
        Assert.Empty(console.Lines);
    }

    [Fact]
    public void Warning_RoutesToYellowMarkupLine_NotWriteError()
    {
        // Warning must stay distinguishable from Error so a --json envelope built from the
        // MarkupLine "[yellow]...[/]" tag preserves "warning" as its own level rather than
        // being folded into "error" (JsonConsoleOutput.MapTagToLevel maps yellow -> warning).
        var console = new TestConsoleOutput();
        var logger = new ConsoleOutputLogger(console, verbose: false);

        logger.Warning("MsiAuthoring", "SBOM sidecar generation failed");

        Assert.Empty(console.Errors);
        var entry = Assert.Single(console.MarkupLines);
        Assert.Contains("[yellow]", entry);
        Assert.Contains("SBOM sidecar generation failed", entry);
    }

    [Fact]
    public void Info_RoutesToWriteLine_RegardlessOfVerbose()
    {
        var console = new TestConsoleOutput();
        var logger = new ConsoleOutputLogger(console, verbose: false);

        logger.Info("MsiAuthoring", "Compiling package 'App'");

        var entry = Assert.Single(console.Lines);
        Assert.Contains("Compiling package 'App'", entry);
    }

    [Fact]
    public void Debug_WhenNotVerbose_IsSuppressed()
    {
        var console = new TestConsoleOutput();
        var logger = new ConsoleOutputLogger(console, verbose: false);

        logger.Debug("MsiRecipeBuilder", "Producer 'File' produced 3 row(s).");

        Assert.Empty(console.AllOutput);
    }

    [Fact]
    public void Debug_WhenVerbose_RoutesToGreyMarkupLine()
    {
        var console = new TestConsoleOutput();
        var logger = new ConsoleOutputLogger(console, verbose: true);

        logger.Debug("MsiRecipeBuilder", "Producer 'File' produced 3 row(s).");

        var entry = Assert.Single(console.MarkupLines);
        Assert.Contains("[grey]", entry);
        Assert.Contains("Producer 'File' produced 3 row(s).", entry);
    }

    [Fact]
    public void Log_WithException_IncludesExceptionTypeAndMessageInRenderedLine()
    {
        var console = new TestConsoleOutput();
        var logger = new ConsoleOutputLogger(console, verbose: false);
        var ex = new InvalidOperationException("disk full");

        logger.Log(LogLevel.Error, "CabinetBuilder", "Failed to open file for cabinet I/O.", ex);

        var entry = Assert.Single(console.Errors);
        Assert.Contains(nameof(InvalidOperationException), entry);
        Assert.Contains("disk full", entry);
    }

    [Fact]
    public void Constructor_NullConsole_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ConsoleOutputLogger(null!, verbose: false));
    }
}
