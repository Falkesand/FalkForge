using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FalkForge.Cli.Diff;

/// <summary>
/// Renders a <see cref="PlanDiffResult"/> as Spectre markup, Markdown, or JSON.
/// </summary>
public static class PlanDiffRenderer
{
    // -------------------------------------------------------------------------
    // Spectre console (human text)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes the diff result to <paramref name="output"/> using Spectre markup.
    /// </summary>
    public static void RenderSpectre(PlanDiffResult result, IConsoleOutput output)
    {
        var modeLabel = result.Mode == "bundle" ? "Bundle" : "MSI";
        output.MarkupLine($"[bold]{modeLabel} plan diff[/]: {Escape(result.OldPath)} → {Escape(result.NewPath)}");

        if (!result.HasChanges)
        {
            output.MarkupLine("[green]No differences found.[/]");
            return;
        }

        output.MarkupLine($"[yellow]{result.TotalChanges} change(s) across {result.Sections.Count(s => s.ChangeCount > 0)} section(s)[/]");
        output.WriteLine(string.Empty);

        foreach (var section in result.Sections)
        {
            if (section.ChangeCount == 0)
                continue;

            output.MarkupLine($"[bold underline]{Escape(section.Title)}[/] ({section.ChangeCount} change(s))");

            foreach (var item in section.Items)
            {
                switch (item.Status)
                {
                    case DiffStatus.Added:
                        output.MarkupLine($"  [green]+ {Escape(item.Label)}[/]: {Escape(item.NewValue ?? string.Empty)}");
                        break;

                    case DiffStatus.Removed:
                        output.MarkupLine($"  [red]- {Escape(item.Label)}[/]: {Escape(item.OldValue ?? string.Empty)}");
                        break;

                    case DiffStatus.Changed:
                        output.MarkupLine($"  [yellow]~ {Escape(item.Label)}[/]:");
                        output.MarkupLine($"      [red]- {Escape(item.OldValue ?? string.Empty)}[/]");
                        output.MarkupLine($"      [green]+ {Escape(item.NewValue ?? string.Empty)}[/]");
                        break;

                    case DiffStatus.Unchanged:
                        // Unchanged items are not emitted in Spectre mode to keep output concise.
                        break;
                }
            }

            output.WriteLine(string.Empty);
        }
    }

    // -------------------------------------------------------------------------
    // Markdown (PR comment ready)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders the diff result as Markdown suitable for embedding in a GitHub PR comment.
    /// Returns a string; does not write to the console.
    /// </summary>
    public static string RenderMarkdown(PlanDiffResult result)
    {
        var sb = new StringBuilder();
        var modeLabel = result.Mode == "bundle" ? "Bundle" : "MSI";

        sb.AppendLine($"## {modeLabel} plan diff");
        sb.AppendLine();
        sb.AppendLine($"- **Old:** `{result.OldPath}`");
        sb.AppendLine($"- **New:** `{result.NewPath}`");
        sb.AppendLine();

        if (!result.HasChanges)
        {
            sb.AppendLine("**No differences found.**");
            return sb.ToString();
        }

        sb.AppendLine($"**{result.TotalChanges} change(s)** across {result.Sections.Count(s => s.ChangeCount > 0)} section(s).");
        sb.AppendLine();

        foreach (var section in result.Sections)
        {
            if (section.ChangeCount == 0)
                continue;

            sb.AppendLine($"### {section.Title} ({section.ChangeCount} change(s))");
            sb.AppendLine();

            foreach (var item in section.Items)
            {
                switch (item.Status)
                {
                    case DiffStatus.Added:
                        sb.AppendLine($"+ **{item.Label}**: `{item.NewValue}`");
                        break;

                    case DiffStatus.Removed:
                        sb.AppendLine($"- **{item.Label}**: `{item.OldValue}`");
                        break;

                    case DiffStatus.Changed:
                        sb.AppendLine($"~ **{item.Label}**:");
                        sb.AppendLine($"  - was: `{item.OldValue}`");
                        sb.AppendLine($"  - now: `{item.NewValue}`");
                        break;

                    case DiffStatus.Unchanged:
                        break;
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // JSON envelope
    // -------------------------------------------------------------------------

    /// <summary>
    /// Serializes <paramref name="result"/> as a <see cref="PlanDiffJsonEnvelope"/> JSON string.
    /// </summary>
    public static string RenderJson(PlanDiffResult result)
    {
        var sections = result.Sections
            .Select(s => new PlanDiffSectionJson(
                s.Title,
                s.ChangeCount,
                s.Items.Select(i => new PlanDiffItemJson(
                    i.Status.ToString().ToLowerInvariant(),
                    i.Label,
                    i.OldValue,
                    i.NewValue)).ToList()))
            .ToList();

        var envelope = new PlanDiffJsonEnvelope(
            PlanDiffJsonEnvelope.CurrentVersion,
            result.Mode,
            result.OldPath,
            result.NewPath,
            result.HasChanges,
            result.TotalChanges,
            sections);

        return JsonSerializer.Serialize(envelope, PlanDiffJsonContext.Default.PlanDiffJsonEnvelope);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static string Escape(string text) => Spectre.Console.Markup.Escape(text);
}

[JsonSerializable(typeof(PlanDiffJsonEnvelope))]
[JsonSerializable(typeof(PlanDiffSectionJson))]
[JsonSerializable(typeof(PlanDiffItemJson))]
[JsonSerializable(typeof(IReadOnlyList<PlanDiffSectionJson>))]
[JsonSerializable(typeof(IReadOnlyList<PlanDiffItemJson>))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PlanDiffJsonContext : JsonSerializerContext;
