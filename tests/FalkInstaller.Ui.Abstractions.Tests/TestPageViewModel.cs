namespace FalkInstaller.Ui.Abstractions.Tests;

using FalkInstaller.Ui.Abstractions.ViewModels;

internal sealed class TestPageViewModel : InstallerPageViewModel
{
    private readonly string _title;
    private readonly string _description;

    public bool AllowNavigateNext { get; set; } = true;
    public bool AllowNavigateBack { get; set; } = true;
    public int NavigatedToCount { get; private set; }
    public int NavigatingFromCount { get; private set; }

    public TestPageViewModel(
        IInstallerEngine engine,
        INavigationService navigation,
        string title = "Test Page",
        string description = "A test page")
        : base(engine, navigation)
    {
        _title = title;
        _description = description;
    }

    public override string Title => _title;
    public override string Description => _description;

    public override bool CanNavigateNext() => AllowNavigateNext;
    public override bool CanNavigateBack() => AllowNavigateBack;

    public override Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        NavigatedToCount++;
        return Task.CompletedTask;
    }

    public override Task OnNavigatingFromAsync(CancellationToken ct = default)
    {
        NavigatingFromCount++;
        return Task.CompletedTask;
    }
}
