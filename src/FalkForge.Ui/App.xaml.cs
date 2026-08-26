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

        // Turnkey built-in UI host (#56): the engine spawns FalkForge.Ui.exe with
        // --manifest/--pipe/--secret-pipe. Parse those, build the wizard window, and connect back
        // to the spawning engine. Missing/unreadable manifest fails loud instead of showing a
        // blank window.
        var resolved = BuiltInUiHost.ResolveArgs(e.Args);
        if (resolved.IsFailure)
        {
            FailLoud(resolved.Error.Message);
            return;
        }

        var manifest = BuiltInUiHost.LoadManifest(resolved.Value.ManifestPath);
        if (manifest.IsFailure)
        {
            FailLoud(manifest.Error.Message);
            return;
        }

        // The launch task cannot be awaited here (WPF must reach its message loop), but it must
        // not be discarded either: a discarded task swallowed every startup exception, so a
        // failing installer showed no window, printed nothing and still exited 0.
        _ = BuiltInUiHost.RunAndReportFailureAsync(
            BuiltInUiHost.LaunchAsync(this, resolved.Value, manifest.Value),
            ex => FailLoud(
                "The installer UI failed to start."
                + Environment.NewLine + Environment.NewLine
                + ex.Message));
    }

    private void FailLoud(string message)
    {
        MessageBox.Show(message, "FalkForge Installer", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
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