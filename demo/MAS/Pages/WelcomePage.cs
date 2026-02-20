using MAS.Views;

namespace MAS.Pages;

public sealed class WelcomePage : MasPageBase<WelcomeView>
{
    public override string Title => "Welcome to MultiAccess setup";
    public override bool CanGoBack => false;
    public override bool ShowPreviousButton => false;
}
