using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Implements <c>forge rules explain &lt;ruleId&gt;</c>.
/// Prints full metadata (Title, Description, Severity, Section) for a single rule.
/// Exits with code 1 if the rule ID is not found.
/// </summary>
public sealed class RulesExplainCommand : Command<RulesExplainSettings>
{
    private readonly IConsoleOutput _console;

    public RulesExplainCommand() : this(new SpectreConsoleOutput()) { }

    public RulesExplainCommand(IConsoleOutput console)
    {
        _console = console;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] RulesExplainSettings settings, CancellationToken cancellationToken)
    {
        // Search all targets for the rule ID — rules can come from any catalog.
        var id = new RuleId(settings.RuleId.Trim().ToUpperInvariant());

        ValidationRule? found = null;
        foreach (var target in Enum.GetValues<ValidationTarget>())
        {
            var rules = ModelValidator.ListRules(target);
            found = rules.FirstOrDefault(r => r.Id == id);
            if (found is not null)
                break;
        }

        if (found is null)
        {
            _console.WriteError($"Rule '{settings.RuleId}' not found. Use 'forge rules list' to see available rules.");
            return ExitCodes.ValidationFailure;
        }

        var severityColor = found.Severity switch
        {
            Severity.Error   => "red",
            Severity.Warning => "yellow",
            _                => "grey"
        };

        _console.MarkupLine($"[bold]{Markup.Escape(found.Id.Value)}[/]  [{severityColor}]{found.Severity}[/]  {Markup.Escape(found.Section.ToString())}");
        _console.MarkupLine($"[bold]Title:[/] {Markup.Escape(found.Title)}");
        _console.MarkupLine($"[bold]Description:[/] {Markup.Escape(found.Description)}");

        return ExitCodes.Success;
    }
}
