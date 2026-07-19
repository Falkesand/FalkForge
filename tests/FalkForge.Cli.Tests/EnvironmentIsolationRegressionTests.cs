using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Merge Gate regression (BLOCKING A): an earlier revision of this branch validated
/// SOURCE_DATE_EPOCH eagerly in <c>Program.cs</c> before any command dispatched, so a malformed
/// ambient value -- set by an unrelated tool following the cross-tool reproducible-builds.org
/// convention, or left over in the shell from a previous <c>--reproducible</c> build -- broke
/// EVERY <c>forge</c> invocation, including commands that never touch SOURCE_DATE_EPOCH at all.
/// That blanket pre-dispatch gate was removed; validation happens only where the value is
/// actually consumed (<c>EnvVarCatalog.TryGetSourceDateEpoch</c>'s callers). This test pins an
/// unrelated command (<c>forge rules list</c> -- pure in-memory, no relationship to
/// SOURCE_DATE_EPOCH) succeeding regardless of the ambient environment.
/// </summary>
[Collection("SourceDateEpoch")]
public sealed class EnvironmentIsolationRegressionTests
{
    [Fact]
    public void RulesListCommand_SucceedsDespiteMalformedAmbientSourceDateEpoch()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "not-a-number");
        try
        {
            var console = new TestConsoleOutput();
            var command = new RulesListCommand(console);
            var settings = new RulesListSettings();
            var context = new CommandContext([], new EmptyRemainingArguments(), "list", null);

            var exitCode = command.ExecuteSync(context, settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, exitCode);
            Assert.DoesNotContain(console.Errors, e => e.Contains("RPR001") || e.Contains("RPR002"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }
}
