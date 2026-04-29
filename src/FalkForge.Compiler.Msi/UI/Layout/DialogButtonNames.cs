using System.Collections.Frozen;
using System.Collections.Generic;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Canonical mapping from <see cref="DialogButton"/> values to the MSI control
/// <c>Name</c> emitted by stock dialog builders.
/// </summary>
/// <remarks>
/// The mapping is required by <see cref="DialogComposer"/> when applying
/// <see cref="DialogCustomizationModel.ButtonLabelOverrides"/>: each override
/// targets a logical button (e.g. <see cref="DialogButton.Browse"/>) and the
/// composer must rewrite the matching control's text. The Browse button is
/// emitted as <c>ChangeFolder</c> by <c>InstallDirDlgBuilder</c>, mirroring the
/// legacy <c>SharedDialogBuilders</c> name; all other buttons use their enum
/// name verbatim.
/// </remarks>
internal static class DialogButtonNames
{
    /// <summary>Frozen lookup keyed by <see cref="DialogButton"/>.</summary>
    public static readonly FrozenDictionary<DialogButton, string> Map = BuildMap();

    private static FrozenDictionary<DialogButton, string> BuildMap()
    {
        var entries = new Dictionary<DialogButton, string>
        {
            [DialogButton.Next] = "Next",
            [DialogButton.Back] = "Back",
            [DialogButton.Cancel] = "Cancel",
            [DialogButton.Install] = "Install",
            [DialogButton.Finish] = "Finish",
            [DialogButton.Browse] = "ChangeFolder",
            [DialogButton.Print] = "Print",
            [DialogButton.Remove] = "Remove",
            [DialogButton.Repair] = "Repair",
        };

        return entries.ToFrozenDictionary();
    }
}
