using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

/// <summary>
/// Final page showing success or failure. On success, displays a congratulations message.
/// On failure, shows the error detail. Matches the WiX BA FinishView.
/// </summary>
public sealed class CompletionPage : MasPageBase<CompletionView>
{
    private bool InstallSuccess => SharedState.Get<bool>("InstallSuccess") is true;

    public override string Title => Localize(
        InstallSuccess
            ? "Completion.SuccessTitle"
            : "Completion.FailureTitle");

    public override bool CanGoBack => false;
    public override bool CanGoNext => true;
    public override bool ShowPreviousButton => false;
    public override string NextButtonText => Localize("Completion.FinishButton");

    public override PageResult OnNext() => PageResult.Finish;

    public string CompletionMessage => Localize(
        InstallSuccess
            ? "Completion.SuccessBody"
            : "Completion.FailureBody");

    public string ErrorDetail => SharedState.Get<string>("InstallError") ?? string.Empty;

    public bool ShowError => !InstallSuccess;
}