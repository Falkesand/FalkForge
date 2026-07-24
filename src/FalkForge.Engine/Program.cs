namespace FalkForge.Engine;

using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Engine.Layout;

internal static partial class Program
{
    // Shared with BootstrapperRunner (RunAsBootstrapper's init-pipe secret delivery uses the same
    // constants as Main's own init-pipe receive block below) — internal rather than moved, so both
    // call sites keep compiling.
    internal const int SecretLength = 32;
    internal static readonly TimeSpan InitPipeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Trust-configuration seam (C18). A publisher who rebuilds FalkForge.Engine can supply a partial
    /// implementation (e.g. <c>Program.TrustConfig.cs</c>) that registers additional trusted publisher
    /// keys via <see cref="Integrity.EngineTrustAnchor"/> — the code-path counterpart to the
    /// <c>-p:FalkForgeTrustedKey</c> build parameter. It runs at the very top of <see cref="Main"/>,
    /// before any bundle is extracted or verified, so the effective trusted set (baked ∪ code-registered)
    /// is fully established before it is frozen on first use. With no implementation this compiles to a
    /// no-op (the default build behaves exactly as before). Registration reads only compiled code — never
    /// bundle/manifest/network input — so it does not reopen the C14 trust-anchor hole.
    /// </summary>
    static partial void ConfigureTrust();

    private static async Task<int> Main(string[] args)
    {
        // Establish any code-registered trusted keys BEFORE any verification path runs (C18). This unions
        // with the MSBuild-baked set; the effective set freezes on its first read below.
        ConfigureTrust();

        // Parse the logging-related flags up front. Other flags continue to be parsed
        // inline below for backward compatibility with the rest of the engine pipeline.
        var argsResult = ProgramArgs.Parse(args);
        if (!argsResult.IsSuccess)
        {
            await Console.Error.WriteLineAsync($"Error: {argsResult.ErrorMessage}");
            return argsResult.SuggestedExitCode;
        }

        var programArgs = argsResult.Value;

        var inv = EngineInvocationArgs.Parse(args);
        var pipeName = inv.PipeName;
        var secretPipeName = inv.SecretPipeName;
        var manifestPath = inv.ManifestPath;
        var planOnly = inv.PlanOnly;
        var planOutputPath = inv.PlanOutputPath;
        var sbomOutputPath = inv.SbomOutputPath;
        var baseBundlePath = inv.BaseBundlePath;
        var requireSigned = inv.RequireSigned;

        // Self-extraction mode: list or extract payloads and exit
        if (inv.ExtractList || inv.ExtractDir is not null)
        {
            return await SelfExtractionMode.RunAsync(inv);
        }

        // Bootstrapper mode: if we ARE the bundle, extract and orchestrate
        if (manifestPath is null && EngineProgramHelpers.HasEmbeddedBundle())
        {
            var bootstrapperArgs = BootstrapperArgs.Parse(args);
            return await BootstrapperRunner.RunAsync(programArgs, bootstrapperArgs, baseBundlePath, requireSigned);
        }

        if (manifestPath is null)
        {
            await Console.Error.WriteLineAsync("Usage: FalkForge.Engine --manifest <path> [--pipe <name> --secret-pipe <name>] [--plan-only [--plan-output <path>]]");
            return 1;
        }

        // Early exit: SBOM extraction (before pipe setup or platform checks).
        // Load manifest inline here to avoid touching EngineSession for this special case.
        if (sbomOutputPath is not null)
        {
            try
            {
                var json = await File.ReadAllBytesAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.InstallerManifest)
                               ?? throw new InvalidOperationException("Manifest deserialized to null.");
                return EngineProgramHelpers.ExtractSbom(manifest, sbomOutputPath);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Failed to load manifest: {ex.Message}");
                return 1;
            }
        }

        // Receive shared secret via init-pipe (avoids passing secrets on the command line).
        PipeConnectionOptions? pipeOptions = null;
        if (pipeName is not null && secretPipeName is not null)
        {
            var secret = new byte[SecretLength];
            try
            {
                using var initPipe = new NamedPipeClientStream(".", secretPipeName, PipeDirection.In);
                using var connectCts = new CancellationTokenSource(InitPipeTimeout);
                await initPipe.ConnectAsync(connectCts.Token);

                var totalRead = 0;
                while (totalRead < SecretLength)
                {
                    var read = await initPipe.ReadAsync(secret.AsMemory(totalRead));
                    if (read == 0)
                    {
                        await Console.Error.WriteLineAsync("Parent closed init pipe before sending full secret.");
                        return 1;
                    }

                    totalRead += read;
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Failed to receive secret: {ex.Message}");
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
            await Console.Error.WriteLineAsync("FalkForge.Engine requires Windows.");
            return 1;
        }

        // ── EngineSession facade ────────────────────────────────────────────
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await using var session = EngineSession.BindToPipe(
            pipeName,
            manifestPath,
            new EngineSessionOptions
            {
                PipeOptions = pipeOptions,
                LogPath = programArgs.LogPath,
                MinimumLogLevel = programArgs.MinimumLogLevel,
                IsPlanOnly = planOnly,
                PlanOnlyOutputPath = planOutputPath
            });

        // Print the session correlation id so operators can grep all three log files
        // (UI, Engine, Elevation) for the same id. Safe: Guid "D" format is fixed-length
        // and contains only hex digits and hyphens — no injection risk.
        await Console.Out.WriteLineAsync($"Session: {session.CorrelationId:D}");

        var outcome = await session.RunUntilShutdown(cts.Token);
        return EngineProgramHelpers.ToExitCode(outcome.State);
    }

}
