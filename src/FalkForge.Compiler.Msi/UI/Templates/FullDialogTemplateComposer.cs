using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

/// <summary>Shared composition for the Mondo and Advanced wizard chains.</summary>
internal static class FullDialogTemplateComposer
{
    internal static IReadOnlyList<MsiDialogModel> Compose(PackageModel package, ImmutableArray<string> stockChain)
    {
        ArgumentNullException.ThrowIfNull(package);
        var customization = package.DialogCustomization;
        var flow = DialogFlowSplice.Resolve(stockChain, customization);
        var dialogs = new List<MsiDialogModel>();
        foreach (var name in stockChain)
        {
            var context = flow.FlowFor(name);
            var content = name switch
            {
                DialogNames.Welcome => WelcomeDlgBuilder.Build(context),
                DialogNames.InstallScope => InstallScopeDlgBuilder.Build(context),
                DialogNames.LicenseAgreement => LicenseDlgBuilder.Build(context),
                DialogNames.SetupType => SetupTypeDlgBuilder.Build(context),
                DialogNames.Customize => CustomizeDlgBuilder.Build(context),
                DialogNames.InstallDir => InstallDirDlgBuilder.Build(context),
                _ => throw new InvalidOperationException($"Unknown stock dialog '{name}'.")
            };
            dialogs.Add(DialogComposer.Compose(content, Layouts.Standard370x270, customization));
        }

        foreach (var content in new[]
        {
            ProgressDlgBuilder.Build(new DialogFlowContext { IncludeStatusLabel = false }),
            ExitDlgBuilder.Build(), CancelDlgBuilder.Build(), BrowseDlgBuilder.Build()
        })
            dialogs.Add(DialogComposer.Compose(content, Layouts.Standard370x270, customization));

        return dialogs;
    }
}
