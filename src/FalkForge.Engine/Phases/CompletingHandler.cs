namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Elevation;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Variables;

public sealed class CompletingHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.Completing;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Tear down the elevated companion process
        await ElevationTeardown.TearDownAsync(context);

        // Save or clear persisted variables on successful completion
        if (context.Manifest.Variables.Length > 0)
        {
            if (context.RequestedAction is InstallAction.Uninstall)
            {
                VariablePersistence.ClearPersistedVariables(
                    context.Manifest.BundleId,
                    context.Manifest.Scope,
                    context.Platform.Registry);
            }
            else
            {
                VariablePersistence.SavePersistedVariables(
                    context.Variables,
                    context.Manifest.BundleId,
                    context.Manifest.Scope,
                    context.Manifest.Variables,
                    context.Platform.Registry);
            }
        }

        // Dispose secrets -- zero memory regardless of success/failure
        context.Variables.DisposeSecrets();

        // Cleanup: ensure exit code reflects success if no error was set
        if (context.ErrorMessage is null)
        {
            context.ExitCode = 0;
        }

        return EnginePhase.Shutdown;
    }
}
