namespace FalkForge.Ui.Localization;

internal sealed record UiLocalizationConfig(
    UiStringResolver Resolver,
    bool AllowLanguageSelection);