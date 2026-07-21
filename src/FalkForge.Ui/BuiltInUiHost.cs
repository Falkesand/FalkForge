using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.ViewModels;
using FalkForge.Ui.Views;

namespace FalkForge.Ui;

/// <summary>
/// Turnkey entry logic for the standalone <c>FalkForge.Ui.exe</c> built-in installer host.
/// <para>
/// The bundle engine (<c>FalkForge.Engine.exe</c>) extracts this executable from the bundle,
/// spawns it with <c>--manifest &lt;path&gt; --pipe &lt;name&gt; --secret-pipe &lt;name&gt;</c>, and
/// then serves the engine data pipe. This host reads the shared secret from the engine-provided
/// init pipe (symmetric to the engine's own secret client in <c>FalkForge.Engine/Program.cs</c>),
/// connects an <see cref="EngineClient"/> to the data pipe, and renders the built-in wizard
/// (<see cref="MainWindow"/> bound to <see cref="DefaultShellViewModel"/>).
/// </para>
/// <para>
/// This is the engine-spawned ("engine-first") counterpart to <see cref="InstallerApp.Run"/>,
/// which is the UI-first path used by custom-UI installers (demos 11-14): there the UI process
/// spawns its own engine. The two share manifest loading and the <see cref="EngineClient"/> /
/// <see cref="DefaultShellViewModel"/> building blocks but not the process-launch direction, so
/// this host does not reuse <see cref="InstallerApp"/>'s engine-spawning code.
/// </para>
/// </summary>
internal static class BuiltInUiHost
{
    private const int SecretLength = 32;
    private static readonly TimeSpan SecretTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Pure, headless-testable command-line resolution. The built-in host requires a
    /// <c>--manifest</c> path (the engine always supplies one); a missing or blank value is a
    /// misuse that must fail loud rather than show a blank window. <c>--pipe</c> and
    /// <c>--secret-pipe</c> are optional: when both are present the host connects to the spawning
    /// engine, otherwise it opens in design/preview mode against the manifest alone.
    /// </summary>
    internal static Result<BuiltInUiArgs> ResolveArgs(string[] args)
    {
        string? manifestPath = null;
        string? pipeName = null;
        string? secretPipeName = null;

        for (var i = 0; i < args.Length - 1; i++)
            switch (args[i].ToLowerInvariant())
            {
                case "--manifest":
                    manifestPath = args[i + 1];
                    break;
                case "--pipe":
                    pipeName = args[i + 1];
                    break;
                case "--secret-pipe":
                    secretPipeName = args[i + 1];
                    break;
            }

        if (string.IsNullOrWhiteSpace(manifestPath))
            return Result<BuiltInUiArgs>.Failure(
                ErrorKind.Validation,
                "FalkForge.Ui requires a --manifest <path> argument. This is the built-in installer "
                + "UI host; it is launched by the FalkForge engine and cannot run on its own.");

        return new BuiltInUiArgs(manifestPath, pipeName, secretPipeName);
    }

    /// <summary>
    /// Loads and deserializes the installer manifest referenced by <paramref name="manifestPath"/>.
    /// A read or parse failure is surfaced as a loud <see cref="Result{T}"/> failure so the entry
    /// point can report it instead of silently degrading.
    /// </summary>
    internal static Result<InstallerManifest> LoadManifest(string manifestPath)
    {
        try
        {
            var json = File.ReadAllBytes(manifestPath);
            var manifest = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.InstallerManifest);
            return manifest is null
                ? Result<InstallerManifest>.Failure(
                    ErrorKind.Validation, $"Manifest '{manifestPath}' deserialized to null.")
                : manifest;
        }
        catch (Exception ex)
        {
            return Result<InstallerManifest>.Failure(
                ErrorKind.FileNotFound, $"Failed to load manifest '{manifestPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the built-in wizard window bound to a fresh <see cref="DefaultShellViewModel"/> over
    /// <paramref name="engine"/>. Factored out so the STA wiring can be verified in isolation.
    /// </summary>
    internal static MainWindow BuildWindow(IInstallerEngine engine)
    {
        return new MainWindow(new DefaultShellViewModel(engine));
    }

    /// <summary>
    /// Resolves the engine, shows the built-in wizard window, then connects to the engine and runs
    /// initial detection. Connection and detection failures are non-fatal: the window stays up in
    /// design-time mode, mirroring <see cref="InstallerApp"/>'s embedded path.
    /// </summary>
    internal static async Task LaunchAsync(Application app, BuiltInUiArgs args, InstallerManifest manifest)
    {
        var engine = await ResolveEngineAsync(args, manifest);

        var window = BuildWindow(engine);
        app.MainWindow = window;
        window.Show();

        if (engine is EngineClient client)
        {
            var connectResult = await client.ConnectAsync();
            if (connectResult.IsFailure)
            {
                // Connection failure is non-fatal; the UI stays in design-time mode.
            }
        }

        if (window.DataContext is DefaultShellViewModel shell)
            try
            {
                await shell.InitializeAsync();
            }
            catch
            {
                // Detection failure is non-fatal for UI startup.
            }
    }

    /// <summary>
    /// Produces a connected <see cref="EngineClient"/> when the engine supplied both a data pipe
    /// and a secret pipe; otherwise a manifest-backed <see cref="NullInstallerEngine"/> so the
    /// window still renders the product's branding in design/preview mode.
    /// </summary>
    private static async Task<IInstallerEngine> ResolveEngineAsync(BuiltInUiArgs args, InstallerManifest manifest)
    {
        if (args.PipeName is null || args.SecretPipeName is null)
            return new NullInstallerEngine(manifest);

        var secret = await ReadSecretAsync(args.SecretPipeName);
        if (secret is null)
            return new NullInstallerEngine(manifest);

        var options = new PipeConnectionOptions
        {
            PipeName = args.PipeName,
            SharedSecret = secret
        };

        return new EngineClient(options, manifest);
    }

    /// <summary>
    /// Reads the one-shot shared secret the engine delivers over its init pipe. Symmetric to the
    /// engine's own secret client (<c>FalkForge.Engine/Program.cs</c>): the engine is the pipe
    /// server, this host the client. Returns <see langword="null"/> on timeout or short read so the
    /// caller degrades to design-time mode rather than crashing.
    /// </summary>
    private static async Task<byte[]?> ReadSecretAsync(string secretPipeName)
    {
        try
        {
            var secret = new byte[SecretLength];
            using var initPipe = new NamedPipeClientStream(".", secretPipeName, PipeDirection.In);
            using var cts = new CancellationTokenSource(SecretTimeout);
            await initPipe.ConnectAsync(cts.Token);

            var totalRead = 0;
            while (totalRead < SecretLength)
            {
                var read = await initPipe.ReadAsync(secret.AsMemory(totalRead), cts.Token);
                if (read == 0)
                    return null;
                totalRead += read;
            }

            return secret;
        }
        catch
        {
            // Secret delivery failure degrades to design-time mode (handled by the caller).
            return null;
        }
    }
}

/// <summary>
/// Parsed command line for the built-in UI host: an always-present manifest path plus the optional
/// engine data-pipe and secret-pipe names.
/// </summary>
internal readonly record struct BuiltInUiArgs(string ManifestPath, string? PipeName, string? SecretPipeName);
