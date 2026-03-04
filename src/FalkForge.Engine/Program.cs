namespace FalkForge.Engine;

using System.IO.Pipes;
using System.Text.Json;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Platform.Windows;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? pipeName = null;
        string? secretPipeName = null;
        string? manifestPath = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    pipeName = args[++i];
                    break;
                case "--secret-pipe":
                    secretPipeName = args[++i];
                    break;
                // SECURITY: DEPRECATED — --secret is accepted for backward compatibility but the
                // value is discarded. The engine uses the init-pipe pattern (like Engine.Elevation)
                // to receive secrets over a short-lived pipe instead of command-line arguments,
                // which are visible in process listings and event logs.
                case "--secret":
                    _ = args[++i]; // consume and discard
                    break;
                case "--manifest":
                    manifestPath = args[++i];
                    break;
            }
        }

        if (manifestPath is null)
        {
            Console.Error.WriteLine("Usage: FalkForge.Engine --manifest <path> [--pipe <name> --secret-pipe <name>]");
            return 1;
        }

        InstallerManifest manifest;
        try
        {
            var json = await File.ReadAllBytesAsync(manifestPath);
            manifest = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.InstallerManifest)
                       ?? throw new InvalidOperationException("Manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load manifest: {ex.Message}");
            return 1;
        }

        PipeConnectionOptions? pipeOptions = null;
        if (pipeName is not null && secretPipeName is not null)
        {
            var secret = new byte[32];
            try
            {
                using var initPipe = new NamedPipeClientStream(".", secretPipeName, PipeDirection.In);
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await initPipe.ConnectAsync(connectCts.Token);

                var totalRead = 0;
                while (totalRead < 32)
                {
                    var read = await initPipe.ReadAsync(secret.AsMemory(totalRead));
                    if (read == 0)
                    {
                        Console.Error.WriteLine("Parent closed init pipe before sending full secret.");
                        return 1;
                    }

                    totalRead += read;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to receive secret: {ex.Message}");
                return 1;
            }

            pipeOptions = new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = secret
            };
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("FalkForge.Engine requires Windows.");
            return 1;
        }

        var platform = new WindowsPlatformServices();
        await using var host = new EngineHost(manifest, platform, pipeOptions);
        return await host.RunAsync();
    }
}
