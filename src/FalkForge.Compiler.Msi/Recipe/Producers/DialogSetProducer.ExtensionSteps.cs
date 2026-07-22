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
    private void AppendInsertedExtensionStepDialogs(PackageModel package, List<MsiDialogModel> dialogs)
    {
        if (_extensionStepBuilders.Count == 0
            || package.DialogCustomization is not { } customization
            || customization.InsertedSteps.IsDefaultOrEmpty)
        {
            return;
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

        DialogBuildContext context = DialogBuildContext.Create(customization, registry);

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (InsertedDialogStep step in customization.InsertedSteps)
        {
            if (registry.TryGet(step.StepName, out IMsiDialogStepBuilder? builder)
                && builder is not null
                && emitted.Add(step.StepName))
            {
                dialogs.Add(builder.Build(context));
            }
        }
    }
}
