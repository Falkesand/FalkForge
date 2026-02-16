namespace FalkForge.Ui.Abstractions.ViewModels;

public abstract class InstallerShellViewModel : INavigationService
{
    private readonly List<InstallerPageViewModel> _pages = new();
    private int _currentPageIndex = -1;

    public IInstallerEngine Engine { get; }

    /// <summary>
    /// Gets a value indicating whether the installer is in maintenance mode
    /// (product is already installed).
    /// </summary>
    public bool IsMaintenanceMode { get; protected set; }

    protected InstallerShellViewModel(IInstallerEngine engine)
    {
        Engine = engine;
    }

    public InstallerPageViewModel? CurrentPage =>
        _currentPageIndex >= 0 && _currentPageIndex < _pages.Count ? _pages[_currentPageIndex] : null;

    public bool CanGoBack => _currentPageIndex > 0 && (CurrentPage?.CanNavigateBack() ?? false);
    public bool CanGoNext => _currentPageIndex < _pages.Count - 1 && (CurrentPage?.CanNavigateNext() ?? false);

    public IReadOnlyList<InstallerPageViewModel> Pages => _pages.AsReadOnly();

    protected void RegisterPage(InstallerPageViewModel page)
    {
        _pages.Add(page);
        if (_currentPageIndex < 0)
            _currentPageIndex = 0;
    }

    public async void NavigateNext()
    {
        if (!CanGoNext) return;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatingFromAsync();
        _currentPageIndex++;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatedToAsync();
        OnCurrentPageChanged();
    }

    public async void NavigateBack()
    {
        if (!CanGoBack) return;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatingFromAsync();
        _currentPageIndex--;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatedToAsync();
        OnCurrentPageChanged();
    }

    public async void NavigateTo(InstallerPageViewModel page)
    {
        var index = _pages.IndexOf(page);
        if (index < 0) return;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatingFromAsync();
        _currentPageIndex = index;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatedToAsync();
        OnCurrentPageChanged();
    }

    public void NavigateTo<T>() where T : InstallerPageViewModel
    {
        var page = _pages.OfType<T>().FirstOrDefault();
        if (page is not null)
            NavigateTo(page);
    }

    protected virtual void OnCurrentPageChanged() { }
}
