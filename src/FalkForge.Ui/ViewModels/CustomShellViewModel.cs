using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Localization;

namespace FalkForge.Ui.ViewModels;

internal sealed class CustomShellViewModel : INotifyPropertyChanged
{
    private readonly IInstallerEngine _engine;
    private readonly List<InstallerPage> _pages;
    private readonly InstallerState _sharedState;
    private int _currentPageIndex = -1;
    private bool _isApplying;
    private string? _statusMessage;

    public CustomShellViewModel(
        IReadOnlyList<InstallerPage> pages,
        IInstallerEngine engine,
        InstallerState sharedState)
    {
        _pages = new List<InstallerPage>(pages);
        _engine = engine;
        _sharedState = sharedState;

        NextCommand = new RelayCommand(OnNextAsync);
        BackCommand = new RelayCommand(OnBackAsync);
        CancelCommand = new RelayCommand(OnCancelAsync);

        if (engine is EngineClient engineClient)
        {
            engineClient.UpdateAvailable += async (version, notes) =>
            {
                if (CurrentPage is InstallerPage page)
                    await page.DispatchUpdateAvailableAsync(version, notes);
            };
            engineClient.UpdateDownloadProgress += async (pct, bytes, total) =>
            {
                if (CurrentPage is InstallerPage page)
                    await page.DispatchUpdateProgressAsync(pct, bytes, total);
            };
            engineClient.UpdateReady += async version =>
            {
                if (CurrentPage is InstallerPage page)
                    await page.DispatchUpdateReadyAsync(version);
            };
        }
    }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CancelCommand { get; }

    public InstallerPage? CurrentPage =>
        _currentPageIndex >= 0 && _currentPageIndex < _pages.Count
            ? _pages[_currentPageIndex]
            : null;

    public FrameworkElement? CurrentView { get; private set; }

    public FrameworkElement? LanguageSelector { get; private set; }

    public bool CanGoBack => !_isApplying && _currentPageIndex > 0 && (CurrentPage?.CanGoBack ?? false);
    public bool CanGoNext => !_isApplying && (CurrentPage?.CanGoNext ?? false);

    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            _isApplying = value;
            RaiseAllNavigationProperties();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CloseRequested;

    internal void InitializeLocalization(UiLocalizationConfig config)
    {
        if (!config.AllowLanguageSelection) return;

        var selector = new LanguageSelectorControl();
        selector.Initialize(config.Resolver);
        LanguageSelector = selector;
        OnPropertyChanged(nameof(LanguageSelector));

        config.Resolver.CultureChanged += () =>
        {
            foreach (var page in _pages)
                page.NotifyCultureChanged();
        };
    }

    public async Task NavigateToFirstPageAsync()
    {
        if (_pages.Count == 0) return;
        _currentPageIndex = 0;
        await ActivateCurrentPageAsync();
    }

    public async Task OnNextAsync()
    {
        if (CurrentPage is null) return;
        var result = CurrentPage.OnNext();
        await ProcessResultAsync(result);
    }

    public async Task OnBackAsync()
    {
        if (CurrentPage is null) return;
        var result = CurrentPage.OnBack();
        await ProcessResultAsync(result);
    }

    public async Task OnCancelAsync()
    {
        if (_isApplying)
            _engine.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    internal async Task ProcessResultAsync(PageResult result)
    {
        StatusMessage = null;

        switch (result.Kind)
        {
            case PageResultKind.Next:
                if (_currentPageIndex < _pages.Count - 1)
                {
                    await NavigateFromCurrentAsync();
                    _currentPageIndex++;
                    await ActivateCurrentPageAsync();
                }

                break;

            case PageResultKind.Previous:
                if (_currentPageIndex > 0)
                {
                    await NavigateFromCurrentAsync();
                    _currentPageIndex--;
                    await ActivateCurrentPageAsync();
                }

                break;

            case PageResultKind.Stay:
                StatusMessage = result.Message;
                break;

            case PageResultKind.GoTo:
                var targetIndex = FindPageIndex(result.TargetType);
                if (targetIndex >= 0)
                {
                    await NavigateFromCurrentAsync();
                    _currentPageIndex = targetIndex;
                    await ActivateCurrentPageAsync();
                }

                break;

            case PageResultKind.Install:
                await ExecuteEngineActionAsync(InstallAction.Install);
                break;

            case PageResultKind.Uninstall:
                await ExecuteEngineActionAsync(InstallAction.Uninstall);
                break;

            case PageResultKind.Repair:
                await ExecuteEngineActionAsync(InstallAction.Repair);
                break;

            case PageResultKind.Finish:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;

            case PageResultKind.Cancel:
                await OnCancelAsync();
                break;
        }
    }

    private async Task ExecuteEngineActionAsync(InstallAction action)
    {
        IsApplying = true;
        try
        {
            var page = CurrentPage;

            // Detect phase
            if (page is not null && !await page.OnDetectBeginAsync())
                return;
            var detectResult = await _engine.DetectAsync();
            if (page is not null)
                await page.OnDetectCompleteAsync(detectResult);

            // Plan phase
            if (page is not null && !await page.OnPlanBeginAsync(action))
                return;
            var planResult = await _engine.PlanAsync(action);
            if (page is not null)
                await page.OnPlanCompleteAsync(planResult);

            // Apply phase
            if (page is not null && !await page.OnApplyBeginAsync())
                return;
            var applyResult = await _engine.ApplyAsync();
            if (page is not null)
                await page.OnApplyCompleteAsync(applyResult);

            // Auto-advance to next page after successful apply
            if (_currentPageIndex < _pages.Count - 1)
            {
                await NavigateFromCurrentAsync();
                _currentPageIndex++;
                await ActivateCurrentPageAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Engine action failed: {ex}");
            StatusMessage = "An unexpected error occurred. See logs for details.";
        }
        finally
        {
            IsApplying = false;
        }
    }

    private async Task ActivateCurrentPageAsync()
    {
        var page = CurrentPage;
        if (page is null) return;

        page.PropertyChanged += OnCurrentPagePropertyChanged;
        CurrentView = page.CreateViewInternal();
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(CurrentView));
        RaiseAllNavigationProperties();
        await page.OnNavigatedToAsync();
    }

    private async Task NavigateFromCurrentAsync()
    {
        if (CurrentPage is not null)
        {
            CurrentPage.PropertyChanged -= OnCurrentPagePropertyChanged;
            await CurrentPage.OnNavigatingFromAsync();
        }
    }

    private void OnCurrentPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstallerPage.CanGoNext) or nameof(InstallerPage.CanGoBack))
            RaiseAllNavigationProperties();
    }

    private int FindPageIndex(Type? pageType)
    {
        if (pageType is null) return -1;
        for (var i = 0; i < _pages.Count; i++)
            if (_pages[i].GetType() == pageType)
                return i;
        return -1;
    }

    private void RaiseAllNavigationProperties()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsApplying));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}