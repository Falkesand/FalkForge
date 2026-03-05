using MAS.Views;

namespace MAS.Pages;

public sealed class WelcomePage : MasPageBase<WelcomeView>
{
    public override string Title => Localize("Welcome.Title");
    public override bool CanGoBack => false;
    public override bool ShowPreviousButton => false;

    public string Body => Localize("Welcome.Body");
}