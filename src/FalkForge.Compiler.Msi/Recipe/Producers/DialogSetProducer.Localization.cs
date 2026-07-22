using FalkForge.Compiler.Msi.UI;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

// !(loc.X) control-text resolution (mirrors legacy DialogEmitter.BuildStringResolver).
internal sealed partial class DialogSetProducer
{
    private static Result<Unit> ResolveLocalizationRefs(
        IReadOnlyList<MsiDialogModel> dialogs,
        PackageModel package,
        string? defaultCultureOverride)
    {
        Localization.LocalizedStringResolver? resolver;

        if (package.LocalizationData.Count == 0)
        {
            var builder = new Localization.LocalizationBuilder();
            builder.AddBuiltInCultures();
            builder.DefaultCulture("en-US");
            Result<System.Collections.Generic.IReadOnlyList<Localization.LocalizationModel>> buildResult = builder.Build();
            if (buildResult.IsFailure)
            {
                return Result<Unit>.Failure(buildResult.Error);
            }

            // Pass built-in list directly — no projection needed, types already match. The
            // resolver default stays the primary culture; an override (per-culture rebuild) is
            // applied as the *requested* culture below so untranslated strings fall back to it.
            resolver = new Localization.LocalizedStringResolver(buildResult.Value, "en-US");
        }
        else
        {
            // Index-based loop — avoids LINQ enumerator and delegate allocation.
            IReadOnlyList<Models.LocalizationData> locData = package.LocalizationData;
            var locModels = new List<Localization.LocalizationModel>(locData.Count);
            for (int i = 0; i < locData.Count; i++)
            {
                locModels.Add(new Localization.LocalizationModel
                {
                    Culture = locData[i].Culture,
                    Strings = locData[i].Strings,
                });
            }

            // The resolver default is always the primary (first configured) culture. MsiAuthoring
            // passes an override to rebuild the UI in each additional culture for the transform
            // diff; that override is the *requested* culture (below), never the resolver default —
            // so a partially-translated culture falls back to the primary for untranslated strings
            // (correct MSI language-transform semantics) instead of failing LOC003.
            resolver = new Localization.LocalizedStringResolver(locModels, locModels[0].Culture);
        }

        // Null for the base MSI (resolves purely as the primary culture); the requested culture for
        // each per-culture rebuild, so its fallback chain ends at the primary culture.
        string? requestedCulture = defaultCultureOverride;

        for (int di = 0; di < dialogs.Count; di++)
        {
            MsiDialogModel dialog = dialogs[di];
            for (int ci = 0; ci < dialog.Controls.Count; ci++)
            {
                MsiControlModel control = dialog.Controls[ci];
                if (control.Text is not null && control.Text.Contains("!(loc."))
                {
                    Result<string> r = resolver.Resolve(control.Text, requestedCulture);
                    if (r.IsFailure)
                    {
                        return Result<Unit>.Failure(r.Error);
                    }

                    control.Text = r.Value;
                }
            }
        }

        return Unit.Value;
    }
}
