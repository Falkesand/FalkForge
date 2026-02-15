namespace FalkForge.Ui.Abstractions.ViewModels;

public abstract class InstallerPageViewModel
{
    public abstract string Title { get; }
    public abstract string Description { get; }
    public IInstallerEngine Engine { get; }
    public INavigationService Navigation { get; }

    protected InstallerPageViewModel(IInstallerEngine engine, INavigationService navigation)
    {
        Engine = engine;
        Navigation = navigation;
    }

    public virtual Task OnNavigatedToAsync(CancellationToken ct = default) => Task.CompletedTask;
    public virtual Task OnNavigatingFromAsync(CancellationToken ct = default) => Task.CompletedTask;
    public virtual bool CanNavigateNext() => true;
    public virtual bool CanNavigateBack() => true;
}
