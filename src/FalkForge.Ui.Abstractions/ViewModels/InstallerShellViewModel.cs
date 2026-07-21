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

    public async Task NavigateNext()
    {
        if (!CanGoNext) return;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatingFromAsync();
        _currentPageIndex++;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatedToAsync();
        OnCurrentPageChanged();
    }

    public async Task NavigateBack()
    {
        if (!CanGoBack) return;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatingFromAsync();
        _currentPageIndex--;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatedToAsync();
        OnCurrentPageChanged();
    }

    public async Task NavigateTo(InstallerPageViewModel page)
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

    public async Task NavigateTo<T>() where T : InstallerPageViewModel
    {
        var page = _pages.OfType<T>().FirstOrDefault();
        if (page is not null)
            await NavigateTo(page);
    }

    /// <summary>
    /// Inserts a page at the given index without navigating. Used to fold in pages discovered at
    /// runtime (e.g. after detection) at a fixed position in the wizard flow. The index is clamped
    /// into range; the current page stays the same page (its index shifts if the insertion is at or
    /// before it).
    /// </summary>
    protected void InsertPage(int index, InstallerPageViewModel page)
    {
        if (index < 0)
            index = 0;
        if (index > _pages.Count)
            index = _pages.Count;

        _pages.Insert(index, page);

        if (_currentPageIndex < 0)
            _currentPageIndex = 0;
        else if (index <= _currentPageIndex)
            _currentPageIndex++;
    }

    /// <summary>
    /// Inserts a page after the current page and navigates to it.
    /// </summary>
    protected async Task InsertPageAfterCurrentAndNavigateAsync(InstallerPageViewModel page)
    {
        var insertIndex = _currentPageIndex + 1;
        _pages.Insert(insertIndex, page);

        if (CurrentPage is not null)
            await CurrentPage.OnNavigatingFromAsync();

        _currentPageIndex = insertIndex;

        if (CurrentPage is not null)
            await CurrentPage.OnNavigatedToAsync();

        OnCurrentPageChanged();
    }

    protected virtual void OnCurrentPageChanged() { }
}
