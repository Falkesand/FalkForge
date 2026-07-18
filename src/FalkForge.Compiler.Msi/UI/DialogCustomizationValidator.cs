using System.Collections.Frozen;
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
/// DLG002 — a suppressed <see cref="StockDialog"/> is a navigation target of another
///           dialog in the same template. Suppressing such a dialog leaves the wizard
///           with a dangling NewDialog event that points to a dialog that will never be
///           emitted, causing the wizard to stall.
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
    // Per-template set of stock dialogs that are navigation targets of other
    // dialogs in the same sequence. Suppressing any of these breaks the flow.
    // The sets are conservative: they list every dialog that another dialog in
    // the template navigates TO via a NewDialog or EndDialog event.
    private static readonly FrozenDictionary<MsiDialogSet, FrozenSet<StockDialog>> ProtectedDialogs =
        BuildProtectedDialogs();

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

        // DLG002 — suppressed dialogs must not be navigation targets.
        if (customization.SuppressedDialogs.Count > 0
            && ProtectedDialogs.TryGetValue(dialogSet, out var protected_))
        {
            foreach (var suppressed in customization.SuppressedDialogs)
            {
                if (protected_.Contains(suppressed))
                {
                    errors.Add(new DialogValidationError(
                        "DLG002",
                        $"Cannot suppress '{suppressed}' dialog in the {dialogSet} template: " +
                        $"it is a navigation target of another dialog in the same set. " +
                        $"Suppressing it would leave a dangling NewDialog event."));
                }
            }
        }

        // DLG003 — bitmap/icon customization keys must resolve to a registered Binary.
        CheckBitmapKey(errors, binaries, customization.BannerBitmap, nameof(DialogCustomizationModel.BannerBitmap));
        CheckBitmapKey(errors, binaries, customization.DialogBitmap, nameof(DialogCustomizationModel.DialogBitmap));
        CheckBitmapKey(errors, binaries, customization.HeaderIcon, nameof(DialogCustomizationModel.HeaderIcon));

        return errors;
    }

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

    private static FrozenDictionary<MsiDialogSet, FrozenSet<StockDialog>> BuildProtectedDialogs()
    {
        // Each template's protected set is the union of all dialogs that appear as
        // NewDialog targets in that template's event wiring. These were extracted from
        // the builder DialogFlowContext chains in FeatureTreeDialogTemplate,
        // InstallDirDialogTemplate, MondoDialogTemplate, AdvancedDialogTemplate, and
        // MinimalDialogTemplate.
        //
        // Entry-point dialogs (Welcome) are also protected because they are referenced
        // from the install sequence Execute action to start the UI sequence.
        return new Dictionary<MsiDialogSet, FrozenSet<StockDialog>>
        {
            [MsiDialogSet.Minimal] = FrozenSet.Create(
                StockDialog.Welcome,    // UI sequence entry point
                StockDialog.Progress,   // target of Welcome→Next
                StockDialog.Exit),      // target of Progress completion

            [MsiDialogSet.InstallDir] = FrozenSet.Create(
                StockDialog.Welcome,    // UI sequence entry point
                StockDialog.InstallDir, // target of Welcome→Next
                StockDialog.Progress,   // target of InstallDir→Next (Install)
                StockDialog.Exit),      // target of Progress completion

            [MsiDialogSet.FeatureTree] = FrozenSet.Create(
                StockDialog.Welcome,    // UI sequence entry point
                StockDialog.License,    // target of Welcome→Next
                StockDialog.Features,   // target of License→Next
                StockDialog.Progress,   // target of Customize→Next (Install)
                StockDialog.Exit),      // target of Progress completion

            [MsiDialogSet.Mondo] = FrozenSet.Create(
                StockDialog.Welcome,    // UI sequence entry point
                StockDialog.License,    // target of Welcome→Next
                StockDialog.InstallDir, // target of SetupType→Next (one branch)
                StockDialog.Features,   // target of SetupType→Next (another branch)
                StockDialog.Progress,   // target of InstallDir/Features→Next (Install)
                StockDialog.Exit),      // target of Progress completion

            [MsiDialogSet.Advanced] = FrozenSet.Create(
                StockDialog.Welcome,    // UI sequence entry point
                StockDialog.License,    // target of Welcome→Next
                StockDialog.InstallDir, // navigation target
                StockDialog.Features,   // navigation target
                StockDialog.Progress,   // target of install branch
                StockDialog.Exit),      // target of Progress completion
        }.ToFrozenDictionary();
    }
}
