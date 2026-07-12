using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

/// <summary>
/// Covers the <see cref="FalkForge.Builders.EnvironmentVariableBuilder"/> Part authoring surface:
/// the fluent Append/Prepend/Set helpers set Part and Action together, and assigning Action
/// directly clears Part so the encoder can never be handed a stale, contradictory pair (the encoder
/// treats a non-null Part as authoritative).
/// </summary>
public sealed class EnvironmentVariablePartBuilderTests
{
    [Fact]
    public void Append_SetsActionAppendPartLastAndSeparator()
    {
        var package = InstallerTestHost.BuildPackage(p =>
            p.EnvironmentVariable("VAR", "v", e => e.Append(":")));

        var ev = Assert.Single(package.EnvironmentVariables);
        Assert.Equal(EnvironmentVariableAction.Append, ev.Action);
        Assert.Equal(EnvironmentVariablePart.Last, ev.Part);
        Assert.Equal(":", ev.Separator);
    }

    [Fact]
    public void Prepend_SetsActionPrependPartFirst()
    {
        var package = InstallerTestHost.BuildPackage(p =>
            p.EnvironmentVariable("VAR", "v", e => e.Prepend()));

        var ev = Assert.Single(package.EnvironmentVariables);
        Assert.Equal(EnvironmentVariableAction.Prepend, ev.Action);
        Assert.Equal(EnvironmentVariablePart.First, ev.Part);
    }

    [Fact]
    public void AssigningActionAfterAppend_ClearsPartSoTheyCannotDisagree()
    {
        // A stale Part from an earlier Append() must not silently override a freshly-set Action —
        // the encoder treats a non-null Part as authoritative, so the builder clears it on Action set.
        var package = InstallerTestHost.BuildPackage(p =>
            p.EnvironmentVariable("VAR", "v", e =>
            {
                e.Append();
                e.Action = EnvironmentVariableAction.Prepend;
            }));

        var ev = Assert.Single(package.EnvironmentVariables);
        Assert.Equal(EnvironmentVariableAction.Prepend, ev.Action);
        Assert.Null(ev.Part);
    }
}
