using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using FalkForge.Cli.Settings;
using FalkForge.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Implements <c>forge rules list</c>.
/// Lists validation rules for the given target, optionally filtered by section, severity, or prefix.
/// Use <c>--json</c> for machine-readable output (full metadata including description).
/// </summary>
public sealed class RulesListCommand : Command<RulesListSettings>
{
    private readonly IConsoleOutput _console;
    private readonly System.IO.TextWriter _jsonSink;

    public RulesListCommand() : this(new SpectreConsoleOutput()) { }

    public RulesListCommand(IConsoleOutput console, System.IO.TextWriter? jsonSink = null)
    {
        _console = console;
        _jsonSink = jsonSink ?? Console.Out;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] RulesListSettings settings, CancellationToken cancellationToken)
    {
        var target = ParseTarget(settings.Target);
        var rules = ModelValidator.ListRules(target);

        // Apply in-memory filters.
        var filtered = rules.AsEnumerable();

        if (settings.Section is { Length: > 0 } sectionFilter)
        {
            if (Enum.TryParse<ModelSection>(sectionFilter, ignoreCase: true, out var section))
                filtered = filtered.Where(r => r.Section == section);
            else
            {
                _console.WriteError($"Unknown section '{sectionFilter}'. Valid values: {string.Join(", ", Enum.GetNames<ModelSection>())}");
                return ExitCodes.ValidationFailure;
            }
        }

        if (settings.Severity is { Length: > 0 } severityFilter)
        {
            if (Enum.TryParse<Severity>(severityFilter, ignoreCase: true, out var severity))
                filtered = filtered.Where(r => r.Severity == severity);
            // Validation already guards the severity value, so no else needed.
        }

        if (settings.Prefix is { Length: > 0 } prefix)
            filtered = filtered.Where(r => r.Id.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        var ruleList = filtered.ToList();

        if (settings.Json)
        {
            var dtos = ruleList.Select(r => new RuleDto(
                r.Id.Value,
                r.Severity.ToString(),
                r.Section.ToString(),
                r.Title,
                r.Description)).ToList();
            _jsonSink.WriteLine(JsonSerializer.Serialize(dtos, RulesJsonContext.Default.ListRuleDto));
            return ExitCodes.Success;
        }

        // Table output.
        if (ruleList.Count == 0)
        {
            _console.MarkupLine("[yellow]No rules match the specified filters.[/]");
            return ExitCodes.Success;
        }

        foreach (var rule in ruleList)
        {
            var severityColor = rule.Severity switch
            {
                Severity.Error   => "red",
                Severity.Warning => "yellow",
                _                => "grey"
            };

            _console.MarkupLine(
                $"[bold]{Markup.Escape(rule.Id.Value)}[/]  " +
                $"[{severityColor}]{rule.Severity,-8}[/]  " +
                $"{Markup.Escape(rule.Section.ToString()),-18}  " +
                $"{Markup.Escape(rule.Title)}");
        }

        return ExitCodes.Success;
    }

    private static ValidationTarget ParseTarget(string target) => target.ToLowerInvariant() switch
    {
        "merge"     => ValidationTarget.MergeModule,
        "patch"     => ValidationTarget.Patch,
        "transform" => ValidationTarget.Transform,
        _           => ValidationTarget.Package
    };
}

// ── JSON DTOs ────────────────────────────────────────────────────────────────

internal sealed record RuleDto(
    [property: JsonPropertyName("id")]          string Id,
    [property: JsonPropertyName("severity")]    string Severity,
    [property: JsonPropertyName("section")]     string Section,
    [property: JsonPropertyName("title")]       string Title,
    [property: JsonPropertyName("description")] string Description);

[JsonSerializable(typeof(List<RuleDto>))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class RulesJsonContext : JsonSerializerContext;
