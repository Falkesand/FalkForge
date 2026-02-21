namespace FalkForge.Engine.Elevation;

/// <summary>
/// Shared helper for tearing down the elevated companion process and its communication resources.
/// Called by both <see cref="Phases.CompletingHandler"/> and <see cref="Phases.ShutdownHandler"/>
/// to avoid duplicated teardown logic.
/// </summary>
internal static class ElevationTeardown
{
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Disposes the elevation client and pipe, waits for the elevated process to exit,
    /// and nulls out all three context fields. Safe to call multiple times.
    /// </summary>
    public static async Task TearDownAsync(EngineContext context)
    {
        if (context.ElevationClient is { } elevationClient)
        {
            await elevationClient.DisposeAsync();
            context.ElevationClient = null;
        }

        if (context.ElevationPipe is { } elevationPipe)
        {
            await elevationPipe.DisposeAsync();
            context.ElevationPipe = null;
        }

        if (context.ElevatedProcess is { } elevatedProcess)
        {
            if (!elevatedProcess.HasExited)
            {
                using var waitCts = new CancellationTokenSource(ProcessExitTimeout);
                try
                {
                    await elevatedProcess.WaitForExitAsync(waitCts.Token);
                }
                catch (OperationCanceledException)
                {
                    elevatedProcess.Kill(entireProcessTree: true);
                }
            }

            elevatedProcess.Dispose();
            context.ElevatedProcess = null;
        }
    }
}
