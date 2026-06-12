using System.Text.Json.Serialization;

namespace FalkForge.Cli.Diff;

/// <summary>
/// Status of a single diffed item within a <see cref="PlanDiffSection"/>.
/// </summary>
public enum DiffStatus
{
    Added,
    Removed,
    Changed,
    Unchanged,
}

/// <summary>
/// A single item within a diff section. <see cref="OldValue"/> and
/// <see cref="NewValue"/> carry detail strings; at least one is non-null.
/// </summary>
public sealed record DiffItem(
    DiffStatus Status,
    string Label,
    string? OldValue,
    string? NewValue);

/// <summary>
/// A named group of related diff items (e.g. "Services", "Registry Entries").
/// </summary>
public sealed record PlanDiffSection(string Title, IReadOnlyList<DiffItem> Items)
{
    /// <summary>Number of items that are not <see cref="DiffStatus.Unchanged"/>.</summary>
    public int ChangeCount => Items.Count(i => i.Status != DiffStatus.Unchanged);
}

/// <summary>
/// Full result of a <c>forge plan diff</c> operation.
/// </summary>
public sealed record PlanDiffResult(
    string Mode,
    string OldPath,
    string NewPath,
    IReadOnlyList<PlanDiffSection> Sections)
{
    /// <summary>True when at least one item in any section is not Unchanged.</summary>
    public bool HasChanges => Sections.Any(s => s.ChangeCount > 0);

    /// <summary>Total number of changed items across all sections.</summary>
    public int TotalChanges => Sections.Sum(s => s.ChangeCount);
}

/// <summary>
/// Machine-readable JSON envelope for <c>forge plan diff --json</c>.
/// </summary>
public sealed record PlanDiffJsonEnvelope(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("oldPath")] string OldPath,
    [property: JsonPropertyName("newPath")] string NewPath,
    [property: JsonPropertyName("hasChanges")] bool HasChanges,
    [property: JsonPropertyName("totalChanges")] int TotalChanges,
    [property: JsonPropertyName("sections")] IReadOnlyList<PlanDiffSectionJson> Sections)
{
    public const int CurrentVersion = 1;
}

public sealed record PlanDiffSectionJson(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("changeCount")] int ChangeCount,
    [property: JsonPropertyName("items")] IReadOnlyList<PlanDiffItemJson> Items);

public sealed record PlanDiffItemJson(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("oldValue")] string? OldValue,
    [property: JsonPropertyName("newValue")] string? NewValue);
