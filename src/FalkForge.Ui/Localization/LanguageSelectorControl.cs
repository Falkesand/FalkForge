namespace FalkForge.Ui.Localization;

using System.Globalization;
using System.Windows.Controls;

internal sealed class LanguageSelectorControl : ComboBox
{
    private UiStringResolver? _resolver;

    public void Initialize(UiStringResolver resolver)
    {
        _resolver = resolver;
        Items.Clear();

        foreach (var culture in resolver.AvailableCultures.OrderBy(c => c))
        {
            try
            {
                var info = CultureInfo.GetCultureInfo(culture);
                Items.Add(new CultureItem(culture, info.NativeName));
            }
            catch (CultureNotFoundException)
            {
                Items.Add(new CultureItem(culture, culture));
            }
        }

        SelectedItem = Items.Cast<CultureItem>()
            .FirstOrDefault(c => c.Code.Equals(resolver.CurrentCulture, StringComparison.OrdinalIgnoreCase));

        SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedItem is CultureItem item)
            _resolver?.SetCulture(item.Code);
    }

    internal sealed record CultureItem(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
