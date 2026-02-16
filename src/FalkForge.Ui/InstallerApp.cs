namespace FalkForge.Ui;

using System.Windows;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.ViewModels;
using FalkForge.Ui.Views;

public static class InstallerApp
{
    public static int Run(string[] args, Action<InstallerUIBuilder> configure)
    {
        var uiBuilder = new InstallerUIBuilder();
        configure(uiBuilder);

        var config = uiBuilder.WindowConfig;
        var pageFactories = uiBuilder.PageFactories;

        if (pageFactories.Count == 0)
            return 1;

        var sharedState = new InstallerState();

        var pages = new List<InstallerPage>(pageFactories.Count);
        foreach (var factory in pageFactories)
        {
            pages.Add(factory());
        }

        var engine = ResolveEngine(args) ?? new NullInstallerEngine();

        foreach (var page in pages)
        {
            page.Engine = engine;
            page.SharedState = sharedState;
            page.DetectedState = engine.DetectedState;
        }

        var viewModel = new CustomShellViewModel(pages, engine, sharedState);

        Window window;
        if (config.CustomWindowType is not null)
        {
            window = (Window)Activator.CreateInstance(config.CustomWindowType)!;
            window.DataContext = viewModel;
        }
        else
        {
            var customWindow = new CustomInstallerWindow();
            customWindow.ApplyConfig(config);
            customWindow.DataContext = viewModel;
            window = customWindow;
        }

        viewModel.CloseRequested += (_, _) => window.Close();

        var app = new Application();

        app.Startup += async (_, _) =>
        {
            try
            {
                await engine.DetectAsync();
                foreach (var page in pages)
                    page.DetectedState = engine.DetectedState;
            }
            catch
            {
                // Detection failure is non-fatal for UI startup
            }

            await viewModel.NavigateToFirstPageAsync();
        };

        app.Run(window);

        return engine.ShutdownAsync().GetAwaiter().GetResult();
    }

    private static IInstallerEngine? ResolveEngine(string[] args)
    {
        string? pipeName = null;
        string? manifestPath = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--pipe":
                    pipeName = args[i + 1];
                    break;
                case "--manifest":
                    manifestPath = args[i + 1];
                    break;
            }
        }

        _ = pipeName;
        _ = manifestPath;

        return null;
    }
}
