namespace FalkForge.Ui.Abstractions.ViewModels;

public interface INavigationService
{
    InstallerPageViewModel? CurrentPage { get; }
    bool CanGoBack { get; }
    bool CanGoNext { get; }
    Task NavigateNext();
    Task NavigateBack();
    Task NavigateTo(InstallerPageViewModel page);
    Task NavigateTo<T>() where T : InstallerPageViewModel;
    IReadOnlyList<InstallerPageViewModel> Pages { get; }
}
