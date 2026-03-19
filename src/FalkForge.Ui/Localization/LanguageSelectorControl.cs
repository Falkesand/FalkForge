using System.Globalization;
using System.Windows.Controls;

namespace FalkForge.Ui.Localization;

internal sealed class LanguageSelectorControl : ComboBox
{
    private bool _initializing;
    private UiStringResolver? _resolver;

    public LanguageSelectorControl()
    {
        SelectionChanged += OnSelectionChanged;
    }

    public void Initialize(UiStringResolver resolver)
    {
        _initializing = true;
        try
        {
            _resolver = resolver;
            Items.Clear();

            foreach (var culture in resolver.AvailableCultures.OrderBy(c => c))
                try
                {
                    var info = CultureInfo.GetCultureInfo(culture);
                    Items.Add(new CultureItem(culture, info.NativeName));
                }
                catch (CultureNotFoundException)
                {
                    Items.Add(new CultureItem(culture, culture));
                }

            SelectedItem = Items.Cast<CultureItem>()
                .FirstOrDefault(c => c.Code.Equals(resolver.CurrentCulture, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _initializing = false;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && SelectedItem is CultureItem item)
            _resolver?.SetCulture(item.Code);
    }

    internal sealed record CultureItem(string Code, string DisplayName)
    {
        public override string ToString()
        {
            return DisplayName;
        }
    }
}