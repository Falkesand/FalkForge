namespace FalkInstaller.Ui.ViewModels;

using System.ComponentModel;
using ReactiveUI;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Ui.Abstractions;
using FalkInstaller.Ui.Abstractions.ViewModels;

public sealed class FeaturesPageViewModel : InstallerPageViewModel, IReactiveObject
{
    private IReadOnlyList<FeatureState> _features = [];

    public FeaturesPageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
    }

    public override string Title => "Features";
    public override string Description => "Select the features you want to install.";

    public IReadOnlyList<FeatureState> Features
    {
        get => _features;
        private set => this.RaiseAndSetIfChanged(ref _features, value);
    }

    public override Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        Features = Engine.Features;
        return Task.CompletedTask;
    }

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanging(PropertyChangingEventArgs args)
        => PropertyChanging?.Invoke(this, args);

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
        => PropertyChanged?.Invoke(this, args);
}
