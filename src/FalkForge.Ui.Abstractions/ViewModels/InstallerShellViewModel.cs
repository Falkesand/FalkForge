namespace FalkForge.Ui.Abstractions.ViewModels;

using System.ComponentModel;

public abstract class InstallerShellViewModel : INavigationService
{
    private readonly List<InstallerPageViewModel> _pages = new();
    private int _currentPageIndex = -1;

    // The page the shell is currently listening to. CanGoBack/CanGoNext read the current page's
    // CanNavigateBack()/CanNavigateNext(), so anything the page changes that those methods read
    // has to reach the shell as a notification or the wizard's buttons never update. Held
    // separately from CurrentPage so the handler is always removed from the page it was added to,
    // even if the page list shifted underneath.
    private InstallerPageViewModel? _observedPage;

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
        {
            _currentPageIndex = 0;
            ObserveCurrentPage();
        }
    }

    public async Task NavigateNext()
    {
        if (!CanGoNext) return;
        if (CurrentPage is not null)
            await CurrentPage.OnNavigatingFromAsync();
        _currentPageIndex++;
        ObserveCurrentPage();
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
        ObserveCurrentPage();
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
        ObserveCurrentPage();
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
        ObserveCurrentPage();

        if (CurrentPage is not null)
            await CurrentPage.OnNavigatedToAsync();

        OnCurrentPageChanged();
    }

    /// <summary>
    /// Moves the shell's <see cref="INotifyPropertyChanged"/> subscription to whichever page is
    /// current now. A page that is no longer current must stop driving the shell: without the
    /// detach, a late notification from an abandoned page would recompute the buttons for a page
    /// the user has already left.
    /// </summary>
    private void ObserveCurrentPage()
    {
        if (ReferenceEquals(_observedPage, CurrentPage))
            return;

        if (_observedPage is INotifyPropertyChanged previous)
            previous.PropertyChanged -= OnCurrentPagePropertyChanged;

        _observedPage = CurrentPage;

        if (_observedPage is INotifyPropertyChanged current)
            current.PropertyChanged += OnCurrentPagePropertyChanged;
    }

    private void OnCurrentPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnNavigationStateChanged();
    }

    protected virtual void OnCurrentPageChanged() { }

    /// <summary>
    /// Called when the current page reports that one of its own properties changed. Override to
    /// re-raise <see cref="CanGoBack"/> and <see cref="CanGoNext"/>: both read the current page's
    /// navigation predicates, so a page that changes what those predicates return (ticking the
    /// licence checkbox, finishing an install) has to be able to update the wizard's buttons.
    /// </summary>
    protected virtual void OnNavigationStateChanged() { }
}
