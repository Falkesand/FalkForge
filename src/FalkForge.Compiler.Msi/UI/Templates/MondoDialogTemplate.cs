using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Templates;

/// <summary>
/// Mondo wizard. Typical and Complete start installation; Custom opens feature and folder selection.
/// </summary>
internal sealed class MondoDialogTemplate : IDialogTemplate
{
    /// <inheritdoc />
    public ImmutableArray<string> StockChain =>
        [DialogNames.Welcome, DialogNames.LicenseAgreement,
            DialogNames.SetupType, DialogNames.Customize, DialogNames.InstallDir];

    public IReadOnlyList<MsiDialogModel> GetDialogs(PackageModel package)
        => FullDialogTemplateComposer.Compose(package, StockChain);
}
