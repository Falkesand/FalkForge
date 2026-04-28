using System.Windows;
using FalkForge.Ui.Themes;

namespace FalkForge.Ui;

public partial class App : Application
{
    /// <summary>
    ///     Gets the maintenance action requested via command-line switches, if any.
    ///     Supported switches: --modify, --repair, --uninstall.
    /// </summary>
    public InstallAction? RequestedAction { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MergeColorDictionary(ThemeDetector.DetectFromSystem());
        RequestedAction = ParseCommandLineAction(e.Args);
    }

    private void MergeColorDictionary(InstallerColorTheme theme)
    {
        var uri = theme switch
        {
            InstallerColorTheme.Dark          => new Uri("Themes/InstallerTheme.Colors.Dark.xaml", UriKind.Relative),
            InstallerColorTheme.HighContrast  => new Uri("Themes/InstallerTheme.Colors.HighContrast.xaml", UriKind.Relative),
            _                                 => new Uri("Themes/InstallerTheme.Colors.Light.xaml", UriKind.Relative),
        };

        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
    }

    internal static InstallAction? ParseCommandLineAction(string[] args)
    {
        foreach (var arg in args)
        {
            var normalized = arg.ToLowerInvariant().TrimStart('-', '/');
            switch (normalized)
            {
                case "modify":
                    return InstallAction.Modify;
                case "repair":
                    return InstallAction.Repair;
                case "uninstall":
                    return InstallAction.Uninstall;
            }
        }

        return null;
    }
}