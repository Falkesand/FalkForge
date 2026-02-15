using System.Windows;

namespace FalkInstaller.Ui;

public partial class App : Application
{
    /// <summary>
    /// Gets the maintenance action requested via command-line switches, if any.
    /// Supported switches: --modify, --repair, --uninstall.
    /// </summary>
    public InstallAction? RequestedAction { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RequestedAction = ParseCommandLineAction(e.Args);
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
