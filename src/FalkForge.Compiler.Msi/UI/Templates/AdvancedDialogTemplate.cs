using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

/// <summary>
/// Advanced wizard. Typical and Complete start installation; Custom opens feature and folder selection.
/// </summary>
internal sealed class AdvancedDialogTemplate : IDialogTemplate
{
    /// <inheritdoc />
    public ImmutableArray<string> StockChain =>
        [DialogNames.Welcome, DialogNames.InstallScope, DialogNames.LicenseAgreement,
            DialogNames.SetupType, DialogNames.Customize, DialogNames.InstallDir];

    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
        => FullDialogTemplateComposer.Compose(package, StockChain);
}
