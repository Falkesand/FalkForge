namespace FalkForge.Ui;

using System.Windows;
using System.Windows.Controls;

public static class PasswordBridge
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached(
            "Key",
            typeof(string),
            typeof(PasswordBridge),
            new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject obj)
        => (string?)obj.GetValue(KeyProperty);

    public static void SetKey(DependencyObject obj, string? value)
        => obj.SetValue(KeyProperty, value);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
            return;

        if (e.OldValue is string oldKey)
            Unregister(box, oldKey);

        if (e.NewValue is string newKey)
            Register(box, newKey);
    }

    private static void Register(PasswordBox box, string key)
    {
        box.Loaded += OnLoaded;
        box.Unloaded += OnUnloaded;

        if (box.IsLoaded)
            TryRegisterWithPage(box, key);
    }

    private static void Unregister(PasswordBox box, string key)
    {
        box.Loaded -= OnLoaded;
        box.Unloaded -= OnUnloaded;
        TryUnregisterFromPage(box, key);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && GetKey(box) is { } key)
            TryRegisterWithPage(box, key);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && GetKey(box) is { } key)
            TryUnregisterFromPage(box, key);
    }

    private static void TryRegisterWithPage(PasswordBox box, string key)
    {
        if (FindInstallerPage(box) is { } page)
            page.RegisterPasswordBox(key, box);
    }

    private static void TryUnregisterFromPage(PasswordBox box, string key)
    {
        if (FindInstallerPage(box) is { } page)
            page.UnregisterPasswordBox(key);
    }

    private static InstallerPage? FindInstallerPage(FrameworkElement element)
        => element.DataContext as InstallerPage;
}
