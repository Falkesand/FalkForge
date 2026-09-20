using FalkForge.Compiler.Msi.UI.Layout.Builders;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

// Extension-contributed dialog steps referenced by DialogCustomization.InsertStep.
internal sealed partial class DialogSetProducer
{
    /// <summary>
    /// Builds and appends the <see cref="MsiDialogModel"/> for each extension-contributed step
    /// named by <see cref="DialogCustomizationModel.InsertedSteps"/> that resolves to an
    /// MSI-capable builder. Each distinct step is emitted once; duplicate insert points (the same
    /// step inserted after two stock dialogs) do not duplicate the dialog rows.
    /// </summary>
    private static bool HasUsableForwardCondition(MsiDialogModel model, MsiControlEventModel candidate)
    {
        if (!string.IsNullOrEmpty(candidate.Condition))
            return candidate.Condition.Trim() == "1";

        // Blank conditions are fallback events. Any potentially true sibling suppresses them;
        // among blank siblings MSI fires only the one with the greatest Ordering value.
        foreach (var sibling in model.Events)
        {
            if (ReferenceEquals(sibling, candidate) || sibling.ControlName != candidate.ControlName)
                continue;
            if (string.IsNullOrEmpty(sibling.Condition))
            {
                if (sibling.Ordering >= candidate.Ordering)
                    return false;
            }
            else if (sibling.Condition.Trim() != "0")
            {
                return false;
            }
        }

        return true;
    }

    private Result<Unit> AppendInsertedExtensionStepDialogs(
        PackageModel package,
        List<MsiDialogModel> dialogs,
        DialogFlowSplice flow,
        ImmutableArray<string> stockChain)
    {
        if (package.DialogCustomization is not { } customization
            || customization.InsertedSteps.IsDefaultOrEmpty)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        // Single registry serves both the name→builder lookup and the DialogBuildContext.
        // The Contains guard tolerates a duplicate-named builder rather than throwing.
        var registry = new DialogStepRegistry();
        for (int i = 0; i < _extensionStepBuilders.Count; i++)
        {
            if (!registry.Contains(_extensionStepBuilders[i].Name))
            {
                registry.Register(_extensionStepBuilders[i]);
            }
        }
        registry.Freeze();

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (InsertedDialogStep step in customization.InsertedSteps)
        {
            // DLG027 — one dialog cannot occupy two positions in one chain. This used to emit a
            // single dialog row and silently drop the second insertion point.
            if (!emitted.Add(step.StepName))
            {
                return Result<Unit>.Failure(
                    ErrorKind.Validation,
                    $"DLG027: dialog step '{step.StepName}' is inserted at more than one anchor. A step "
                    + "occupies one position in the wizard, so it can follow only one dialog. Register a "
                    + "second step builder if two pages are needed.");
            }

            // DLG026 — the anchor names a dialog this set does not contain, so there is nothing to
            // insert after. Silent before: the step was emitted and nothing navigated to it.
            if (!DialogFlowSplice.AnchorIsPresent(stockChain, step.After))
            {
                return Result<Unit>.Failure(
                    ErrorKind.Validation,
                    $"DLG026: dialog step '{step.StepName}' is anchored after {step.After}, which the "
                    + $"{package.DialogSet} dialog set does not contain, so the step would never be "
                    + "reached. Anchor it after a dialog this set has, or use "
                    + $"{nameof(DialogStepAnchor)}.{nameof(DialogStepAnchor.BeforeInstall)}, which every "
                    + "set supports.");
            }

            if (!registry.TryGet(step.StepName, out IMsiDialogStepBuilder? builder) || builder is null)
            {
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"DLG025: dialog step '{step.StepName}' has no MSI-capable builder. " +
                    "Name registration alone cannot emit a dialog; provide an IMsiDialogStepBuilder.");
            }

            // Each step gets the flow its own position in the spliced chain implies, so its
            // Next and Back point at real neighbours rather than at nothing.
            DialogFlowContext stepFlow = flow.FlowFor(step.StepName);
            MsiDialogModel model = builder.Build(DialogBuildContext.Create(customization, registry, stepFlow));
            if (model is null || !string.Equals(model.Name, step.StepName, StringComparison.Ordinal))
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"DLG025: builder for dialog step '{step.StepName}' returned " +
                    $"'{model?.Name ?? "<no dialog>"}'. The emitted dialog must match the registered step name.");

            // DLG025 — the splice is correct by construction for the stock dialogs and correct by
            // COOPERATION for the step, because a builder can simply not consult the flow it was
            // handed. Nothing in the type system enforces it, so check rather than trust: without
            // this the build succeeds and the user reaches a page with no working button.
            DialogControlEvent expected = DialogFooter.NextEvent(stepFlow);

            // Matching the verb and argument alone would make this look like protection it is not.
            // The edge also has to be one the user can actually take: published by a control the
            // dialog defines, on a control they can see and click, and not behind a condition that
            // never fires. Each of those was measured being accepted by a verb-only match while the
            // user was left stuck on the page.
            bool publishesForwardEdge = model.Events.Exists(e =>
                string.Equals(e.Event.Value.Trim(), expected.Event, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Argument.Trim(), expected.Argument, StringComparison.Ordinal)
                && HasUsableForwardCondition(model, e)
                && model.Controls.Exists(c =>
                    string.Equals(c.Name, e.ControlName, StringComparison.Ordinal)
                    && c.Attributes.HasFlag(MsiControlAttributes.Visible)
                    && c.Attributes.HasFlag(MsiControlAttributes.Enabled)));

            if (!publishesForwardEdge)
            {
                return Result<Unit>.Failure(
                    ErrorKind.Validation,
                    $"DLG025: dialog step '{step.StepName}' does not publish the forward navigation it "
                    + $"was given, so the wizard stops on it. Its builder must publish "
                    + $"{expected.Event} with argument '{expected.Argument}' on the control that "
                    + "continues, which it can read from DialogBuildContext.Flow rather than hardcoding. "
                    + "A builder that authors its own flow cannot know its target, because the target "
                    + "depends on the dialog set and on where the author anchored the step.");
            }

            dialogs.Add(model);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}
