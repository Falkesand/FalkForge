using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

// !(loc.X) resolution for dialog Control.Text, dialog Title, and the fixed UIText table entries
// (mirrors legacy DialogEmitter.BuildStringResolver; Title/UIText resolution added in beta.4 —
// previously only Control.Text was resolved, so a localized installer's dialog titles and UIText
// stayed in the primary language for every additional culture).
internal sealed partial class DialogSetProducer
{
    // "UiText.<Key>" -> literal English default, derived once from the fixed UiTextEntries. Seeded
    // into the PRIMARY culture whenever a package supplies custom LocalizationData (which otherwise
    // excludes the built-in en-US/sv-SE strings entirely, per the branch below) so authors are never
    // forced to translate all 21 framework UIText keys just to localize their own dialogs — an
    // author-defined "UiText.X" key in their own data still wins over this default.
    private static readonly IReadOnlyDictionary<string, string> UiTextLocDefaults = BuildUiTextLocDefaults();

    private static Dictionary<string, string> BuildUiTextLocDefaults()
    {
        var defaults = new Dictionary<string, string>(UiTextEntries.Length);
        for (int i = 0; i < UiTextEntries.Length; i++)
        {
            defaults[$"UiText.{UiTextEntries[i].Key}"] = UiTextEntries[i].Text;
        }

        return defaults;
    }

    private static Result<ImmutableArray<(string Key, string Text)>> ResolveLocalizationRefs(
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
                return Result<ImmutableArray<(string, string)>>.Failure(buildResult.Error);
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
                if (i == 0)
                {
                    // Seed the framework UIText defaults under the author's primary-culture
                    // strings; author-defined keys are applied after and therefore win.
                    var primaryStrings = new Dictionary<string, string>(UiTextLocDefaults);
                    foreach (System.Collections.Generic.KeyValuePair<string, string> kv in locData[i].Strings)
                    {
                        primaryStrings[kv.Key] = kv.Value;
                    }

                    locModels.Add(new Localization.LocalizationModel
                    {
                        Culture = locData[i].Culture,
                        Strings = primaryStrings,
                    });
                }
                else
                {
                    locModels.Add(new Localization.LocalizationModel
                    {
                        Culture = locData[i].Culture,
                        Strings = locData[i].Strings,
                    });
                }
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

            if (dialog.Title is not null && dialog.Title.Contains("!(loc."))
            {
                Result<string> titleResult = resolver.Resolve(dialog.Title, requestedCulture);
                if (titleResult.IsFailure)
                {
                    return Result<ImmutableArray<(string, string)>>.Failure(titleResult.Error);
                }

                dialog.Title = titleResult.Value;
            }

            for (int ci = 0; ci < dialog.Controls.Count; ci++)
            {
                MsiControlModel control = dialog.Controls[ci];
                if (control.Text is not null && control.Text.Contains("!(loc."))
                {
                    Result<string> r = resolver.Resolve(control.Text, requestedCulture);
                    if (r.IsFailure)
                    {
                        return Result<ImmutableArray<(string, string)>>.Failure(r.Error);
                    }

                    control.Text = r.Value;
                }
            }
        }

        // UIText rows are a fixed static array shared across every build (never mutated in
        // place — see the ImmutableArray comment on BuiltInCultures in
        // BuiltInLocalizationExtensions for why a shared mutable array would corrupt future
        // builds); resolve into a fresh array local to this call instead.
        ImmutableArray<(string Key, string Text)>.Builder resolvedUiText =
            ImmutableArray.CreateBuilder<(string Key, string Text)>(UiTextEntries.Length);
        for (int i = 0; i < UiTextEntries.Length; i++)
        {
            string key = UiTextEntries[i].Key;
            Result<string> uiTextResult = resolver.Resolve($"!(loc.UiText.{key})", requestedCulture);
            if (uiTextResult.IsFailure)
            {
                return Result<ImmutableArray<(string, string)>>.Failure(uiTextResult.Error);
            }

            resolvedUiText.Add((key, uiTextResult.Value));
        }

        return resolvedUiText.ToImmutable();
    }
}
