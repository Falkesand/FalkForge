using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Validates a <see cref="DialogCustomizationModel"/> against the active dialog set and
/// step registry. Produces <see cref="DialogValidationError"/> instances for each violation.
/// </summary>
/// <remarks>
/// DLG001 — an <see cref="InsertedDialogStep"/> references a step name that is not
///           registered in the <see cref="DialogStepRegistry"/>. Every name in
///           <see cref="DialogCustomizationModel.InsertedSteps"/> must have a matching
///           builder registered before compilation begins.
/// <para>
/// DLG002 — <see cref="DialogCustomizationModel.SuppressedDialogs"/> is non-empty.
///           <see cref="FalkForge.Models.DialogCustomization.SuppressDialog"/> is not
///           implemented (task #44): nothing downstream consumes this set — every stock
///           dialog is composed and emitted regardless of it — so a populated entry
///           silently produces an MSI that ignores the request. The fluent builder method
///           is gated separately with <c>[Obsolete(error: true)]</c>, but
///           <see cref="DialogCustomizationModel.SuppressedDialogs"/> is a public
///           <c>init</c> property, so an object initializer can populate it without ever
///           calling that method. This rule is the only gate covering that path, so it
///           fails the build unconditionally rather than only for navigation-breaking
///           entries.
/// </para>
/// <para>
/// DLG003 — <see cref="DialogCustomizationModel.BannerBitmap"/>, <see cref="DialogCustomizationModel.DialogBitmap"/>,
///           or <see cref="DialogCustomizationModel.HeaderIcon"/> names a Binary stream key that
///           is not registered in <see cref="PackageModel.Binaries"/> (via
///           <c>PackageBuilder.Binary(name, sourcePath)</c>). The synthesized/swapped control's
///           <c>Text</c> would reference a Binary row that does not exist, compiling cleanly but
///           breaking the dialog at runtime (blank or missing image) — Error, not a Warning.
/// </para>
/// </remarks>
internal static class DialogCustomizationValidator
{
    /// <summary>
    /// Validates the customization model and returns any DLG001/DLG002/DLG003 violations.
    /// Returns an empty list when the customization is valid.
    /// </summary>
    /// <param name="binaries">
    /// The package's registered Binary entries (<see cref="PackageModel.Binaries"/>), used by
    /// DLG003 to cross-check <see cref="DialogCustomizationModel.BannerBitmap"/>,
    /// <see cref="DialogCustomizationModel.DialogBitmap"/>, and
    /// <see cref="DialogCustomizationModel.HeaderIcon"/> keys.
    /// </param>
    public static IReadOnlyList<DialogValidationError> Validate(
        DialogCustomizationModel customization,
        MsiDialogSet dialogSet,
        DialogStepRegistry registry,
        IReadOnlyList<BinaryModel> binaries)
    {
        ArgumentNullException.ThrowIfNull(customization);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(binaries);

        var errors = new List<DialogValidationError>();

        // DLG001 — every InsertedStep name must be registered (MSI-capable or name-only).
        foreach (var step in customization.InsertedSteps)
        {
            if (!registry.Contains(step.StepName))
            {
                errors.Add(new DialogValidationError(
                    "DLG001",
                    $"Dialog step '{step.StepName}' is not registered. " +
                    $"Register it via DialogStepRegistry.Register before compiling. " +
                    $"(after: {step.After})"));
            }
        }

        // DLG002 — SuppressDialog is not implemented (task #44). No dialog-set emitter or
        // DialogComposer consumes SuppressedDialogs, so any non-empty set would silently
        // compile into an MSI that shows every stock dialog anyway. Reject unconditionally —
        // this is the only check that also closes the object-initializer path around the
        // Obsolete(error: true) builder method, since SuppressedDialogs is a public init
        // property.
        foreach (var suppressed in customization.SuppressedDialogs)
        {
            errors.Add(new DialogValidationError(
                "DLG002",
                $"DialogCustomization.SuppressDialog({suppressed}) is not implemented (see task #44) " +
                $"and has no effect on the {dialogSet} template's compiled MSI — remove '{suppressed}' from SuppressedDialogs."));
        }

        // DLG003 — bitmap/icon customization keys must resolve to a registered Binary.
        CheckBitmapKey(errors, binaries, customization.BannerBitmap, nameof(DialogCustomizationModel.BannerBitmap));
        CheckBitmapKey(errors, binaries, customization.DialogBitmap, nameof(DialogCustomizationModel.DialogBitmap));
        CheckBitmapKey(errors, binaries, customization.HeaderIcon, nameof(DialogCustomizationModel.HeaderIcon));

        return errors;
    }

    // NOTE (task #24): a per-template FrozenDictionary<MsiDialogSet, FrozenSet<StockDialog>>
    // mapping each dialog set to its "navigation target" dialogs (BuildProtectedDialogs) used
    // to live here for the old navigation-aware DLG002 rule. It was deleted rather than kept
    // unused, because keeping an unread field fails the build (CA1823 / IDE0052 under
    // TreatWarningsAsErrors) and this codebase's convention is no suppression pragmas for a
    // gate-defeating warning. Its content is preserved verbatim in this PR's description and
    // task #44's notes for whoever builds the real suppression feature — note it was also
    // provably wrong (ProtectedDialogs[InstallDir] omitted License even though
    // InstallDirDialogTemplate wires Welcome→License), so re-derive it from the templates
    // rather than pasting it back as-is.

    // Ordinal, exact-match lookup — mirrors how BinaryTableProducer keys the emitted Binary
    // table rows and stream registry by the literal BinaryModel.Name string.
    private static void CheckBitmapKey(
        List<DialogValidationError> errors,
        IReadOnlyList<BinaryModel> binaries,
        string? key,
        string verbName)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        for (var i = 0; i < binaries.Count; i++)
        {
            if (string.Equals(binaries[i].Name, key, StringComparison.Ordinal))
            {
                return;
            }
        }

        errors.Add(new DialogValidationError(
            "DLG003",
            $"DialogCustomization.{verbName}('{key}') references Binary key '{key}' which is not " +
            $"registered. Register it via PackageBuilder.Binary(\"{key}\", <sourcePath>) before compiling."));
    }
}
