using FalkForge.Cli.Commands;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class DecompileCommandMsixTests : IDisposable
{
    private readonly string _tempFile;

    public DecompileCommandMsixTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.msix");
        File.WriteAllBytes(_tempFile, []);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "decompile", null);

    [Fact]
    public void Execute_MsixFile_ReturnsNotSupported()
    {
        var console = new TestConsoleOutput();
        var command = new DecompileCommand(console);
        var settings = new Settings.DecompileSettings { FilePath = _tempFile };

        var result = command.Execute(CreateContext(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, result);
        Assert.Contains(console.Errors, e => e.Contains("not yet supported"));
    }
}
