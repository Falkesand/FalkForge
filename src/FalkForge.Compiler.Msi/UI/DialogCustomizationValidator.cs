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
    /// Validates the customization model and returns any DLG001/DLG002 violations.
    /// Returns an empty list when the customization is valid.
    /// </summary>
    public static IReadOnlyList<DialogValidationError> Validate(
        DialogCustomizationModel customization,
        MsiDialogSet dialogSet,
        DialogStepRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(customization);
        ArgumentNullException.ThrowIfNull(registry);

        var errors = new List<DialogValidationError>();

        // DLG001 — every InsertedStep name must be registered.
        foreach (var step in customization.InsertedSteps)
        {
            if (!registry.TryGet(step.StepName, out _))
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

        return errors;
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
