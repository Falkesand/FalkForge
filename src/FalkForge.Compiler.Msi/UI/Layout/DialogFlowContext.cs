namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Carries the navigation targets a stock-dialog builder needs to wire its declarative
/// events. Builders that participate in a wizard chain receive a populated instance from
/// the calling template; builders for self-contained modals (Cancel, Browse, Exit) do not
/// need one.
/// </summary>
/// <remarks>
/// Phase-6.5 stock builders embed the legacy <c>SharedDialogBuilders.BuildXDlg</c> event
/// sets directly into their <see cref="DialogContent"/> output. The flow targets are
/// template-specific (Minimal goes Welcome → InstallDir → Exit, Mondo goes Welcome →
/// License → SetupType → ...), so the builders take the targets through this small record
/// rather than hard-coding them.
/// </remarks>
public sealed record DialogFlowContext
{
    /// <summary>
    /// Dialog name the wizard advances to when the user clicks Next. Builders that do not
    /// emit a Next event ignore this value.
    /// </summary>
    public string? NextDialog { get; init; }

    /// <summary>
    /// Dialog name the wizard returns to when the user clicks Back. Builders that do not
    /// emit a Back event ignore this value.
    /// </summary>
    public string? BackDialog { get; init; }

    /// <summary>
    /// Dialog spawned when the user clicks Cancel. Defaults to the stock <c>CancelDlg</c>
    /// name so the bulk of templates need not override it.
    /// </summary>
    public string CancelDialog { get; init; } = "CancelDlg";

    /// <summary>
    /// When <c>true</c>, builders that have an optional description block include it in
    /// the produced content. Mirrors the legacy <c>includeDescription</c> parameter.
    /// </summary>
    public bool IncludeDescription { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, the Progress builder includes the StatusLabel control. Mirrors
    /// the legacy <c>includeStatusLabel</c> parameter.
    /// </summary>
    public bool IncludeStatusLabel { get; init; } = true;
}
