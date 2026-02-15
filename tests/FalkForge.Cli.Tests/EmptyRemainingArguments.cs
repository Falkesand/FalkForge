using Spectre.Console.Cli;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Empty <see cref="IRemainingArguments"/> implementation for constructing <see cref="CommandContext"/> in tests.
/// </summary>
internal sealed class EmptyRemainingArguments : IRemainingArguments
{
    public ILookup<string, string?> Parsed => Array.Empty<string>().ToLookup(_ => string.Empty, _ => (string?)null);
    public IReadOnlyList<string> Raw => [];
}
