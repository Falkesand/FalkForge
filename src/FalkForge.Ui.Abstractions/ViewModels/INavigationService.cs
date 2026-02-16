namespace FalkForge.Ui.Abstractions.ViewModels;

public interface INavigationService
{
    InstallerPageViewModel? CurrentPage { get; }
    bool CanGoBack { get; }
    bool CanGoNext { get; }
    void NavigateNext();
    void NavigateBack();
    void NavigateTo(InstallerPageViewModel page);
    void NavigateTo<T>() where T : InstallerPageViewModel;
    IReadOnlyList<InstallerPageViewModel> Pages { get; }
}
