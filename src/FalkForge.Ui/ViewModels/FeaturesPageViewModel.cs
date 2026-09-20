using System.ComponentModel;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

public sealed class FeaturesPageViewModel : InstallerPageViewModel, IReactiveObject
{
    private IReadOnlyList<FeatureState> _features = [];
    private IReadOnlyList<BundleFeatureViewModel> _featureOptions = [];

    public IReadOnlyList<BundleFeatureViewModel> FeatureOptions
    {
        get => _featureOptions;
        private set => this.RaiseAndSetIfChanged(ref _featureOptions, value);
    }

    public FeaturesPageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        ReactiveNotifications.Enable(this);
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
        ct.ThrowIfCancellationRequested();
        Features = Engine.Features.ToArray();
        var channel = Engine as IBundleFeatureChannel;
        FeatureOptions = Features.Select(feature => new BundleFeatureViewModel(
            feature, channel is not null, (id, selected) =>
            {
                channel?.SetFeatureSelection(id, selected);
                Features = Features.Select(state => state.FeatureId == id
                    ? state with { IsSelected = selected }
                    : state).ToArray();
            })).ToArray();
        return Task.CompletedTask;
    }
}