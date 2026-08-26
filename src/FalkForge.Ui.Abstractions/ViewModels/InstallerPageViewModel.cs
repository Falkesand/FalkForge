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

    /// <summary>
    /// When true the shell steps over this page while walking Next and Back. The page stays in
    /// <c>Pages</c> and can still be reached by an explicit <c>NavigateTo</c>; it just is not part
    /// of the straight-line walk for this run.
    /// <para>
    /// Read fresh on every navigation rather than captured once, so a page can become part of the
    /// walk after detection has told it what this machine looks like.
    /// </para>
    /// </summary>
    public virtual bool IsSkippedInLinearFlow => false;

    public virtual Task OnNavigatedToAsync(CancellationToken ct = default) => Task.CompletedTask;
    public virtual Task OnNavigatingFromAsync(CancellationToken ct = default) => Task.CompletedTask;
    public virtual bool CanNavigateNext() => true;
    public virtual bool CanNavigateBack() => true;
}
