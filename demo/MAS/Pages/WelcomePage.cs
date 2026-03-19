using MAS.Views;

namespace MAS.Pages;

/// <summary>
/// First page shown to the user. Displays a welcome message with the product version.
/// Matches the WiX BA StartPageView.
/// </summary>
public sealed class WelcomePage : MasPageBase<WelcomeView>
{
    public override string Title => Localize("Welcome.Title");
    public override bool CanGoBack => false;
    public override bool ShowPreviousButton => false;

    public string Body => Localize("Welcome.Body");
}