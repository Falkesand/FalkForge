using System.ComponentModel;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

/// <summary>
/// Wizard page presenting a per-package MSI feature picker: one section per package that the engine
/// advertised MSI features for, each a tree of selectable features. Toggling a feature sends the
/// updated selection to the engine (<see cref="IPackageMsiFeatureChannel.SetPackageFeatureSelection"/>),
/// which the engine turns into that package's <c>ADDLOCAL</c>.
/// <para>
/// Only registered by the shell when at least one package advertised features, so the page is never
/// shown empty. When the engine does not implement <see cref="IPackageMsiFeatureChannel"/> (design-time
/// / headless), <see cref="Sections"/> is empty.
/// </para>
/// </summary>
public sealed class PackageFeaturesPageViewModel : InstallerPageViewModel, IReactiveObject
{
    private readonly IPackageMsiFeatureChannel? _channel;

    public PackageFeaturesPageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        _channel = engine as IPackageMsiFeatureChannel;
        Sections = BuildSections();
    }

    public override string Title => "Package Features";
    public override string Description => "Choose which features to install for each package.";

    /// <summary>One section per package that advertised MSI features.</summary>
    public IReadOnlyList<PackageFeatureSectionViewModel> Sections { get; }

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

    private IReadOnlyList<PackageFeatureSectionViewModel> BuildSections()
    {
        if (_channel is null || _channel.PackageMsiFeatures.Count == 0)
            return [];

        var sections = new List<PackageFeatureSectionViewModel>(_channel.PackageMsiFeatures.Count);
        foreach (var (packageId, features) in _channel.PackageMsiFeatures)
            sections.Add(new PackageFeatureSectionViewModel(packageId, features, SendSelection));
        return sections;
    }

    private void SendSelection(string packageId, IReadOnlyList<string> selectedFeatureIds)
    {
        _channel?.SetPackageFeatureSelection(packageId, selectedFeatureIds);
    }
}
