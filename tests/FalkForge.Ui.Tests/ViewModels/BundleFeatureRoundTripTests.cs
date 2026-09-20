namespace FalkForge.Ui.Tests.ViewModels;

using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Testing;
using FalkForge.Ui.ViewModels;
using FalkForge.Ui.Views;
using Xunit;

public sealed class BundleFeatureRoundTripTests
{
    [WpfTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SelectionsReachTheRealPlanner(int scenario)
    {
        var manifest = new InstallerManifest
        {
            Name = "Feature test", Manufacturer = "Test", Version = "1.0.0",
            BundleId = Guid.NewGuid(), UpgradeCode = Guid.NewGuid(), Scope = InstallScope.PerUser,
            Packages = new[] { "core", "default", "optional", "shared" }.Select(id => new PackageInfo
            {
                Id = id, Type = PackageType.MsiPackage, DisplayName = id,
                SourcePath = id + ".msi", Sha256Hash = ""
            }).ToArray(),
            Features =
            [
                new("core", "Core", null, false, true, ["core"]),
                new("default", "Default", null, true, false, ["default", "shared"]),
                new("optional", "Optional", null, false, false, ["optional", "shared"])
            ]
        };
        var options = new PipeConnectionOptions
        {
            PipeName = $"falk-features-{Guid.NewGuid():N}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var channel = NamedPipeUiChannel.Create(options);
        await using var pipeline = new InstallerPipelineBuilder().WithUiChannel(channel)
            .WithManifest(manifest).WithRegistry(new MockRegistry()).Build();
        await using var client = new EngineClient(options, manifest);
        var start = channel.StartAsync(timeout.Token);
        Assert.True((await client.ConnectAsync(timeout.Token)).IsSuccess);
        Assert.True((await start).IsSuccess);
        var run = new PipelineRunner(pipeline, channel).RunAsync(timeout.Token);
        await client.DetectAsync(timeout.Token);

        if (scenario != 3) // Also prove the defaults reach planning without visiting the picker.
        {
            var shell = new DefaultShellViewModel(client);
            var vm = shell.Pages.OfType<FeaturesPageViewModel>().Single();
            await vm.OnNavigatedToAsync(timeout.Token);
            var view = new FeaturesPage { DataContext = vm };
            view.Measure(new Size(700, 500));
            view.Arrange(new Rect(0, 0, 700, 500));
            view.UpdateLayout();
            var checkboxes = Descendants<CheckBox>(view).ToDictionary(
                box => Assert.IsType<BundleFeatureViewModel>(box.DataContext).FeatureId);

            Assert.Equal(3, checkboxes.Count);
            Assert.False(checkboxes["core"].IsEnabled);
            Assert.True(checkboxes["core"].IsChecked);
            Assert.True(checkboxes["default"].IsEnabled);
            Assert.True(checkboxes["default"].IsChecked);
            Assert.False(checkboxes["optional"].IsChecked);

            if (scenario is 1 or 2)
            {
                Toggle(checkboxes["default"], false);
                Toggle(checkboxes["optional"], scenario == 1);
            }

            vm.FeatureOptions.Single(feature => feature.IsRequired).IsSelected = false;
            Assert.True(vm.FeatureOptions.Single(feature => feature.IsRequired).IsSelected);
            await vm.OnNavigatedToAsync(timeout.Token);
            Assert.Equal(scenario is 0, vm.FeatureOptions.Single(f => f.FeatureId == "default").IsSelected);
            Assert.Equal(scenario is 1, vm.FeatureOptions.Single(f => f.FeatureId == "optional").IsSelected);
        }

        var plan = await client.PlanAsync(InstallAction.Install, timeout.Token);
        string[] expected = scenario switch
        {
            1 => ["core", "optional", "shared"],
            2 => ["core"],
            _ => ["core", "default", "shared"]
        };
        Assert.Equal(expected, plan.PackageActions);
        Assert.Equal(0, await client.ShutdownAsync().WaitAsync(timeout.Token));
        Assert.Equal(0, await run.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task EngineWithoutSelectionCapability_RemainsReadOnly()
    {
        var engine = new TestInstallerEngine
        {
            Features = [new("optional", "Optional", null, false, false, false, 0)]
        };
        var vm = new FeaturesPageViewModel(engine, new DefaultShellViewModel(engine));
        await vm.OnNavigatedToAsync();
        var option = Assert.Single(vm.FeatureOptions);
        Assert.False(option.IsEnabled);
        option.IsSelected = true;
        Assert.False(option.IsSelected);
    }

    private static void Toggle(CheckBox checkbox, bool selected)
    {
        checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, (bool?)selected);
        checkbox.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }
}
