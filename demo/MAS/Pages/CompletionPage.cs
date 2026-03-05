using MAS.Views;

namespace MAS.Pages;

public sealed class CompletionPage : MasPageBase<CompletionView>
{
    private bool InstallSuccess => SharedState.Get<bool>("InstallSuccess") is true;

    public override string Title => Localize(
        InstallSuccess
            ? "Completion.SuccessTitle"
            : "Completion.FailureTitle");

    public override bool CanGoBack => false;
    public override bool CanGoNext => false;
    public override bool ShowPreviousButton => false;

    public string CompletionMessage => Localize(
        InstallSuccess
            ? "Completion.SuccessBody"
            : "Completion.FailureBody");

    public string ErrorDetail => SharedState.Get<string>("InstallError") ?? string.Empty;

    public bool ShowError => !InstallSuccess;
}