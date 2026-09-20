namespace FalkForge.Ui.Tests.Views;

using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Documents;
using FalkForge.Ui.Tests.ViewModels;
using FalkForge.Ui.ViewModels;
using FalkForge.Ui.Views;
using Xunit;

public sealed class EmbeddedLicenseTests
{
    [WpfTheory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("rtf")]
    public void LicensePage_RendersEmbeddedAgreementUsingTheUiManifestContext(string format)
    {
        const string agreement = "Agreement: åäö 日本語";
        var content = format switch
        {
            "utf16" => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(agreement)).ToArray(),
            "rtf" => Encoding.ASCII.GetBytes(@"{\rtf1\ansi {\b Agreement}: \'e5\'e4\'f6 \u26085?\u26412?\u35486?}"),
            _ => Encoding.UTF8.GetBytes(agreement)
        };
        var engine = new TestInstallerEngine();
        var json = JsonSerializer.Serialize(engine.Manifest with
        {
            LicenseFile = @"Z:\unavailable-build-machine\license.rtf",
            LicenseContent = content
        }, ManifestJsonContext.Default.InstallerManifest);
        engine.Manifest = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest)!;
        var shell = new DefaultShellViewModel(engine);
        var vm = shell.Pages.OfType<LicensePageViewModel>().Single();
        var page = new LicensePage { DataContext = vm };
        var viewer = Assert.IsType<RichTextBox>(page.FindName("LicenseViewer"));
        var document = viewer.Document;
        var displayed = new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd();

        Assert.Equal(agreement, displayed);
        Assert.True(viewer.IsReadOnly);
        Assert.False(vm.IsSkippedInLinearFlow);
        Assert.False(vm.CanNavigateNext());
        vm.IsAccepted = true;
        Assert.True(vm.CanNavigateNext());
        Assert.True(engine.LicenseAccepted);
    }

    [WpfFact]
    public void LegacyManifestWithPathButNoContent_DoesNotDisplayThePathOrAllowAcceptance()
    {
        var engine = new TestInstallerEngine();
        engine.Manifest = engine.Manifest with { LicenseContent = null };
        var shell = new DefaultShellViewModel(engine);
        var vm = shell.Pages.OfType<LicensePageViewModel>().Single();
        var page = new LicensePage { DataContext = vm };
        var viewer = Assert.IsType<RichTextBox>(page.FindName("LicenseViewer"));
        var document = viewer.Document;
        var displayed = new TextRange(document.ContentStart, document.ContentEnd).Text;

        Assert.Contains("No license text available.", displayed, StringComparison.Ordinal);
        Assert.DoesNotContain(engine.Manifest.LicenseFile!, displayed, StringComparison.Ordinal);
        Assert.False(vm.IsSkippedInLinearFlow);
        vm.IsAccepted = true;
        Assert.False(vm.CanAccept);
        Assert.False(vm.IsAccepted);
        Assert.False(vm.CanNavigateNext());
        Assert.False(engine.LicenseAccepted);
    }

    [WpfFact]
    public void RtfWithNoVisibleAgreement_CannotBeAccepted()
    {
        var engine = new TestInstallerEngine();
        engine.Manifest = engine.Manifest with { LicenseContent = "{\\rtf1}"u8.ToArray() };
        var shell = new DefaultShellViewModel(engine);
        var vm = shell.Pages.OfType<LicensePageViewModel>().Single();
        var page = new LicensePage { DataContext = vm };
        var viewer = Assert.IsType<RichTextBox>(page.FindName("LicenseViewer"));
        var document = viewer.Document;

        Assert.Contains("could not be displayed",
            new TextRange(document.ContentStart, document.ContentEnd).Text, StringComparison.Ordinal);
        vm.IsAccepted = true;
        Assert.False(vm.CanAccept);
        Assert.False(vm.CanNavigateNext());
        Assert.False(engine.LicenseAccepted);
    }

    [Fact]
    public async Task UnlicensedBundle_SkipsAcceptanceInBothDirections()
    {
        var engine = new TestInstallerEngine();
        engine.Manifest = engine.Manifest with { LicenseFile = null, LicenseContent = null };
        var shell = new DefaultShellViewModel(engine);
        Assert.IsType<WelcomePageViewModel>(shell.CurrentPage);

        await shell.NavigateNext();
        Assert.IsType<FeaturesPageViewModel>(shell.CurrentPage);
        Assert.Null(engine.LicenseAccepted);

        await shell.NavigateBack();
        Assert.IsType<WelcomePageViewModel>(shell.CurrentPage);
    }
}
