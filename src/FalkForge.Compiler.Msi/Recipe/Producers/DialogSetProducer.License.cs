using System.Text;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

// License RTF injection into the ScrollableText license control.
internal sealed partial class DialogSetProducer
{
    // Marker set by LicenseDlgBuilder on the license ScrollableText control. Windows
    // Installer ignores Control._Property for ScrollableText (it renders the literal
    // Control.Text), so the RTF must be written into Text — the property only serves
    // as the injection target here.
    private const string LicenseControlProperty = "LicenseText";

    /// <summary>
    /// Loads <see cref="PackageModel.LicenseFile"/> and writes its content into the
    /// <c>Text</c> of every ScrollableText license control in the composed dialog set.
    /// </summary>
    /// <remarks>
    /// No-op when no license file is configured. When a license file is set but the
    /// active dialog set has no license page (e.g. <see cref="MsiDialogSet.Minimal"/>),
    /// there is no control to render into, so the method does not fail — the unused file
    /// is not an authoring error — but it does queue a <c>DLG004</c> warning on
    /// <paramref name="context"/> (drained to the build logger by
    /// <see cref="MsiRecipeBuilder"/>) so the author is not left guessing why the license
    /// never shows. A missing or unreadable file <b>is</b> an authoring error and fails
    /// loud with <see cref="ErrorKind.FileNotFound"/>: silently continuing is exactly the
    /// blank-license bug this method fixes.
    /// </remarks>
    private static Result<Unit> InjectLicenseText(
        List<MsiDialogModel> dialogs, PackageModel package, RecipeBuildContext context)
    {
        if (string.IsNullOrEmpty(package.LicenseFile))
        {
            return Unit.Value;
        }

        // Locate the ScrollableText license control(s) the active dialog set placed.
        // Index-based loops avoid IReadOnlyList<T> enumerator allocation (HAA0401).
        List<MsiControlModel>? targets = null;
        for (int di = 0; di < dialogs.Count; di++)
        {
            MsiDialogModel dialog = dialogs[di];
            for (int ci = 0; ci < dialog.Controls.Count; ci++)
            {
                MsiControlModel control = dialog.Controls[ci];
                if (control.Type == MsiControlType.ScrollableText
                    && string.Equals(control.Property, LicenseControlProperty, StringComparison.Ordinal))
                {
                    (targets ??= []).Add(control);
                }
            }
        }

        // LicenseFile set but the dialog set has no license page (Minimal / None). Nothing
        // to render into, so do not read the file and do not fail — but warn (DLG004) so the
        // mismatch is not silent.
        if (targets is null)
        {
            context.AddWarning(
                "DLG004",
                $"LicenseFile is set ('{package.LicenseFile}') but dialog set '{package.DialogSet}' has " +
                "no license page, so the license will not be shown. Use InstallDir, FeatureTree, Mondo, " +
                "or Advanced instead.");
            return Unit.Value;
        }

        string licenseText;
        try
        {
            // RTF is a 7-bit-ASCII container (non-ASCII glyphs are \'xx / \uNNNN escaped),
            // so a byte-preserving Latin-1 decode carries the file into the Control.Text
            // string with no transcoding artefacts — the RTF stays byte-intact.
            byte[] bytes = File.ReadAllBytes(package.LicenseFile);
            licenseText = Encoding.Latin1.GetString(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return Result<Unit>.Failure(
                ErrorKind.FileNotFound,
                $"License file could not be read: '{package.LicenseFile}'. {ex.Message}");
        }

        for (int i = 0; i < targets.Count; i++)
        {
            targets[i].Text = licenseText;
        }

        return Unit.Value;
    }
}
