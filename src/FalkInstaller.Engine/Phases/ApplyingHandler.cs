namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Execution;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Messages;

public sealed class ApplyingHandler : IEnginePhaseHandler
{
    private readonly PackageExecutor _executor;

    public ApplyingHandler(PackageExecutor executor)
    {
        _executor = executor;
    }

    public EnginePhase Phase => EnginePhase.Applying;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        var plan = context.CurrentPlan;
        if (plan is null)
        {
            context.ErrorMessage = "No plan to apply";
            return EnginePhase.Failed;
        }

        var totalPackages = plan.Actions.Count;

        // Notify UI that apply is beginning
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new ApplyBeginMessage
            {
                TotalPackages = totalPackages
            }, ct);
        }

        for (var i = 0; i < totalPackages; i++)
        {
            ct.ThrowIfCancellationRequested();

            var action = plan.Actions[i];

            // Report progress
            if (context.UiPipe is not null && context.UiPipe.IsConnected)
            {
                await context.UiPipe.SendAsync(new ProgressMessage
                {
                    Progress = new InstallProgress(i + 1, totalPackages, action.PackageId)
                }, ct);
            }

            var result = await _executor.ExecuteAsync(action, ct);
            if (result.IsFailure)
            {
                context.ErrorMessage = result.Error.Message;
                context.ExitCode = 1;

                // Notify UI of failure
                if (context.UiPipe is not null && context.UiPipe.IsConnected)
                {
                    await context.UiPipe.SendAsync(new ApplyCompleteMessage
                    {
                        ExitCode = 1,
                        ErrorMessage = result.Error.Message
                    }, ct);
                }

                return EnginePhase.Failed;
            }
        }

        // Notify UI of success
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new ApplyCompleteMessage
            {
                ExitCode = 0
            }, ct);
        }

        return EnginePhase.Completing;
    }
}
