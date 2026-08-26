namespace FalkForge.Ui.Tests.ViewModels;

using System.ComponentModel;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// Every view model in the built-in wizard implements <c>IReactiveObject</c> by hand rather than
/// deriving from ReactiveUI's <c>ReactiveObject</c>, and sets its properties through
/// <c>this.RaiseAndSetIfChanged(...)</c>.
/// <para>
/// That combination silently drops every <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// event. ReactiveUI keeps the per-object event subject in a <c>Lazy&lt;&gt;</c> and
/// <c>ExtensionState.RaisePropertyChanged</c> pushes to it only <c>if
/// (_propertyChanged.IsValueCreated)</c>. <c>ReactiveObject</c> forces that lazy from its own
/// <c>PropertyChanged</c> add-accessor; a hand-written field-like event has no accessor to do it,
/// so the subject is never created and the notification is dropped on the floor. Verified against
/// the decompiled ReactiveUI 23.2.28 and measured: a bare <c>ReactiveObject</c> raised the event
/// while <c>LicensePageViewModel</c>, <c>CompletePageViewModel</c> and the rest raised nothing.
/// </para>
/// <para>
/// The user-visible result was a wizard where nothing the engine reported ever reached the screen:
/// the progress text and percentage, the completion message, and the licence acceptance that
/// enables <c>Next &gt;</c>. Property values did change, so tests that read the property back
/// passed throughout.
/// </para>
/// </summary>
public class ReactiveViewModelNotificationTests
{
    private readonly TestInstallerEngine _engine = new();

    private static List<string?> Record(INotifyPropertyChanged source, Action mutate)
    {
        var seen = new List<string?>();
        source.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
        mutate();
        return seen;
    }

    [Fact]
    public void LicencePage_RaisesPropertyChanged_WhenAccepted()
    {
        var vm = new LicensePageViewModel(_engine, new DefaultShellViewModel(_engine));

        var seen = Record(vm, () => vm.IsAccepted = true);

        Assert.Contains(nameof(vm.IsAccepted), seen);
        Assert.True(_engine.LicenseAccepted);
    }

    [Fact]
    public void CompletePage_RaisesPropertyChanged_WhenMessageSet()
    {
        var vm = new CompletePageViewModel(_engine, new DefaultShellViewModel(_engine));

        var seen = Record(vm, () => vm.Message = "Installed.");

        Assert.Contains(nameof(vm.Message), seen);
    }

    [Fact]
    public void CompletePage_RaisesPropertyChanged_WhenSuccessSet()
    {
        var vm = new CompletePageViewModel(_engine, new DefaultShellViewModel(_engine));

        var seen = Record(vm, () => vm.IsSuccess = true);

        Assert.Contains(nameof(vm.IsSuccess), seen);
    }

    [Fact]
    public void InstallDirPage_RaisesPropertyChanged_WhenDirectorySet()
    {
        var vm = new InstallDirPageViewModel(_engine, new DefaultShellViewModel(_engine));

        var seen = Record(vm, () => vm.InstallDirectory = @"C:\Somewhere\Else");

        Assert.Contains(nameof(vm.InstallDirectory), seen);
    }

    [Fact]
    public async Task FeaturesPage_RaisesPropertyChanged_WhenFeaturesArrive()
    {
        _engine.Features =
        [
            new FalkForge.Engine.Protocol.FeatureState(
                "Main", "Main", Description: null, IsSelected: true, IsRequired: true,
                WasPreviouslyInstalled: false, DiskSpaceRequired: 0)
        ];
        var vm = new FeaturesPageViewModel(_engine, new DefaultShellViewModel(_engine));

        var seen = new List<string?>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
        await vm.OnNavigatedToAsync();

        Assert.Contains(nameof(vm.Features), seen);
    }

}
