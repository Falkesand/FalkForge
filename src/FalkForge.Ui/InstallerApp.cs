using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Plugins;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.ViewModels;
using FalkForge.Ui.Views;

namespace FalkForge.Ui;

public static class InstallerApp
{
    private const int SecretLength = 32;
    private static readonly TimeSpan InitPipeTimeout = TimeSpan.FromSeconds(30);

    public static int Run(string[] args, Action<InstallerUIBuilder> configure)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            var exitCode = 0;
            var thread = new Thread(() => exitCode = RunCore(args, configure));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return exitCode;
        }

        return RunCore(args, configure);
    }

    private static int RunCore(string[] args, Action<InstallerUIBuilder> configure)
    {
        var uiBuilder = new InstallerUIBuilder();
        configure(uiBuilder);

        var pluginRegistry = new PluginServiceRegistry();
        // Drain-and-apply: honour both registration paths; first-wins semantics in
        // PluginServiceRegistry.TryAdd protect against duplicate service types.
        foreach (var plugin in uiBuilder.Plugins)
            plugin.RegisterServices(pluginRegistry);
        uiBuilder.BulkPluginRegistry?.RegisterAll(pluginRegistry);
        pluginRegistry.Freeze();

        var config = uiBuilder.WindowConfig;
        var pageFactories = uiBuilder.PageFactories;

        if (pageFactories.Count == 0)
            return 1;

        var sharedState = new InstallerState(new DpapiDataProtector());

        var pages = new List<InstallerPage>(pageFactories.Count);
        foreach (var factory in pageFactories) pages.Add(factory());

        var engine = ResolveEngine(args) ?? new NullInstallerEngine();

        foreach (var page in pages)
        {
            page.Engine = engine;
            page.SharedState = sharedState;
            page.PluginServices = pluginRegistry;
            page.DetectedState = engine.DetectedState;
        }

        var locConfig = uiBuilder.LocalizationConfig;
        if (locConfig is not null)
            foreach (var page in pages)
                page._stringResolver = locConfig.Resolver;

        var viewModel = new CustomShellViewModel(pages, engine, sharedState);

        if (locConfig is not null)
            viewModel.InitializeLocalization(locConfig);

        var app = new Application();

        app.Startup += async (_, _) =>
        {
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

            app.MainWindow = window;
            window.Show();

            if (engine is EngineClient client)
            {
                var connectResult = await client.ConnectAsync();
                if (connectResult.IsFailure)
                {
                    // Connection failure is non-fatal; UI stays in design-time mode
                }
            }

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

        app.Run();

        return engine.ShutdownAsync().GetAwaiter().GetResult();
    }

    private static IInstallerEngine? ResolveEngine(string[] args)
    {
        string? pipeName = null;
        string? manifestPath = null;

        for (var i = 0; i < args.Length - 1; i++)
            switch (args[i].ToLowerInvariant())
            {
                case "--pipe":
                    pipeName = args[i + 1];
                    break;
                case "--manifest":
                    manifestPath = args[i + 1];
                    break;
            }

        if (pipeName is null || manifestPath is null)
            return null;

        return LaunchEngineAndCreateClient(pipeName, manifestPath);
    }

    private static EngineClient? LaunchEngineAndCreateClient(string pipeName, string manifestPath)
    {
        try
        {
            var json = File.ReadAllBytes(manifestPath);
            var manifest = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest);
            if (manifest is null)
                return null;

            var secret = new byte[SecretLength];
            RandomNumberGenerator.Fill(secret);

            var secretPipeName = $"falkforge_init_{Guid.NewGuid():N}";

            var initPipe = new NamedPipeServerStream(
                secretPipeName, PipeDirection.Out, maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            var enginePath = FindEnginePath();
            if (enginePath is null)
            {
                initPipe.Dispose();
                return null;
            }

            var engineArgs = $"--manifest \"{manifestPath}\" --pipe {pipeName} --secret-pipe {secretPipeName}";

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = enginePath,
                Arguments = engineArgs,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                initPipe.Dispose();
                return null;
            }

            // Deliver secret in the background so we don't block the UI thread.
            // The init pipe is disposed after delivery completes (or fails).
            _ = DeliverSecretAsync(initPipe, secret);

            var pipeOptions = new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = secret
            };

            return new EngineClient(pipeOptions, manifest);
        }
        catch
        {
            // Best-effort: any failure falls back to design-time mode
            return null;
        }
    }

    private static async Task DeliverSecretAsync(NamedPipeServerStream initPipe, byte[] secret)
    {
        try
        {
            using var cts = new CancellationTokenSource(InitPipeTimeout);
            await initPipe.WaitForConnectionAsync(cts.Token);
            await initPipe.WriteAsync(secret.AsMemory(), cts.Token);
            await initPipe.FlushAsync(cts.Token);
        }
        catch
        {
            // Secret delivery failure is handled when ConnectAsync times out
        }
        finally
        {
            await initPipe.DisposeAsync();
        }
    }

    private static string? FindEnginePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "FalkForge.Engine.exe"),
            Path.Combine(baseDir, "..", "FalkForge.Engine", "FalkForge.Engine.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
