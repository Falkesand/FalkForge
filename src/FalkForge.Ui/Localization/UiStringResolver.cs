using FalkForge.Localization;

namespace FalkForge.Ui.Localization;

internal sealed class UiStringResolver
{
    private readonly Dictionary<string, Dictionary<string, string>> _cultures;
    private readonly string _defaultCulture;
    private IReadOnlyList<string> _fallbackChain;

    public UiStringResolver(
        Dictionary<string, Dictionary<string, string>> cultures,
        string defaultCulture)
    {
        _cultures = cultures;
        _defaultCulture = defaultCulture;
        CurrentCulture = defaultCulture;
        _fallbackChain = CultureFallbackChain.Build(defaultCulture, defaultCulture);
    }

    public string CurrentCulture { get; private set; }

    public IReadOnlyCollection<string> AvailableCultures => _cultures.Keys;

    public event Action? CultureChanged;

    public void SetCulture(string culture)
    {
        if (string.Equals(CurrentCulture, culture, StringComparison.OrdinalIgnoreCase))
            return;
        CurrentCulture = culture;
        _fallbackChain = CultureFallbackChain.Build(culture, _defaultCulture);
        CultureChanged?.Invoke();
    }

    public string Resolve(string key)
    {
        foreach (var culture in _fallbackChain)
            if (_cultures.TryGetValue(culture, out var strings) &&
                strings.TryGetValue(key, out var value))
                return value;
        return key;
    }
}