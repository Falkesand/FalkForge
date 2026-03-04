using System.ComponentModel;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

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

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanging(PropertyChangingEventArgs args)
    {
        PropertyChanging?.Invoke(this, args);
    }

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, args);
    }

    public override Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        Features = Engine.Features;
        return Task.CompletedTask;
    }
}