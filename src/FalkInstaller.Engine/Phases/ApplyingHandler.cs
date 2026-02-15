namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Cache;
using FalkInstaller.Engine.Download;
using FalkInstaller.Engine.Execution;
using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Messages;

public sealed class ApplyingHandler : IEnginePhaseHandler
{
    private readonly PackageExecutor _executor;
    private readonly PayloadDownloader? _downloader;
    private readonly PackageCache? _cache;

    public ApplyingHandler(PackageExecutor executor, PayloadDownloader? downloader = null, PackageCache? cache = null)
    {
        _executor = executor;
        _downloader = downloader;
        _cache = cache;
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

        // If we have segments, use segment-aware execution
        if (plan.Segments.Count > 0)
        {
            return await ExecuteWithSegmentsAsync(context, plan, totalPackages, ct);
        }

        // Fallback: flat execution for backwards compatibility
        return await ExecuteFlatAsync(context, plan, totalPackages, ct);
    }

    private async Task<EnginePhase> ExecuteWithSegmentsAsync(
        EngineContext context,
        InstallPlan plan,
        int totalPackages,
        CancellationToken ct)
    {
        var globalActionIndex = 0;

        for (var segmentIndex = 0; segmentIndex < plan.Segments.Count; segmentIndex++)
        {
            var segment = plan.Segments[segmentIndex];

            foreach (var action in segment.Actions)
            {
                // Check for user cancellation between packages
                if (context.UserCancelled)
                {
                    context.ErrorMessage = "Installation cancelled by user";
                    context.ExitCode = 1;
                    context.FailedSegmentIndex = segmentIndex;

                    if (context.UiPipe is not null && context.UiPipe.IsConnected)
                    {
                        await context.UiPipe.SendAsync(new ProgressMessage
                        {
                            Progress = new InstallProgress(globalActionIndex + 1, totalPackages, "Cancelled")
                        }, ct);

                        await context.UiPipe.SendAsync(new ApplyCompleteMessage
                        {
                            ExitCode = 1,
                            ErrorMessage = "Installation cancelled by user"
                        }, ct);
                    }

                    return EnginePhase.RollingBack;
                }

                ct.ThrowIfCancellationRequested();

                // Report progress
                if (context.UiPipe is not null && context.UiPipe.IsConnected)
                {
                    await context.UiPipe.SendAsync(new ProgressMessage
                    {
                        Progress = new InstallProgress(globalActionIndex + 1, totalPackages, action.PackageId)
                    }, ct);
                }

                // Acquire payload if remote
                var acquireResult = await AcquirePayloadIfNeededAsync(action, ct);
                if (acquireResult.IsFailure)
                {
                    context.ErrorMessage = acquireResult.Error.Message;
                    context.ExitCode = 1;
                    context.FailedSegmentIndex = segmentIndex;

                    if (context.UiPipe is not null && context.UiPipe.IsConnected)
                    {
                        await context.UiPipe.SendAsync(new ApplyCompleteMessage
                        {
                            ExitCode = 1,
                            ErrorMessage = acquireResult.Error.Message
                        }, ct);
                    }

                    return EnginePhase.RollingBack;
                }

                var result = await _executor.ExecuteAsync(action, ct);
                if (result.IsFailure)
                {
                    context.ErrorMessage = result.Error.Message;
                    context.ExitCode = 1;
                    context.FailedSegmentIndex = segmentIndex;

                    // Notify UI of failure
                    if (context.UiPipe is not null && context.UiPipe.IsConnected)
                    {
                        await context.UiPipe.SendAsync(new ApplyCompleteMessage
                        {
                            ExitCode = 1,
                            ErrorMessage = result.Error.Message
                        }, ct);
                    }

                    return EnginePhase.RollingBack;
                }

                // Track reboot requirements from execution outcome
                if (result.Value.Behavior is ExitCodeBehavior.RebootRequired
                    or ExitCodeBehavior.ScheduleReboot)
                {
                    context.RebootRequired = true;
                }

                globalActionIndex++;
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

    private async Task<EnginePhase> ExecuteFlatAsync(
        EngineContext context,
        InstallPlan plan,
        int totalPackages,
        CancellationToken ct)
    {
        for (var i = 0; i < totalPackages; i++)
        {
            // Check for user cancellation between packages
            if (context.UserCancelled)
            {
                context.ErrorMessage = "Installation cancelled by user";
                context.ExitCode = 1;

                if (context.UiPipe is not null && context.UiPipe.IsConnected)
                {
                    await context.UiPipe.SendAsync(new ProgressMessage
                    {
                        Progress = new InstallProgress(i + 1, totalPackages, "Cancelled")
                    }, ct);

                    await context.UiPipe.SendAsync(new ApplyCompleteMessage
                    {
                        ExitCode = 1,
                        ErrorMessage = "Installation cancelled by user"
                    }, ct);
                }

                return EnginePhase.RollingBack;
            }

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

            // Acquire payload if remote
            var acquireResult = await AcquirePayloadIfNeededAsync(action, ct);
            if (acquireResult.IsFailure)
            {
                context.ErrorMessage = acquireResult.Error.Message;
                context.ExitCode = 1;

                if (context.UiPipe is not null && context.UiPipe.IsConnected)
                {
                    await context.UiPipe.SendAsync(new ApplyCompleteMessage
                    {
                        ExitCode = 1,
                        ErrorMessage = acquireResult.Error.Message
                    }, ct);
                }

                return EnginePhase.Failed;
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

            // Track reboot requirements from execution outcome
            if (result.Value.Behavior is ExitCodeBehavior.RebootRequired
                or ExitCodeBehavior.ScheduleReboot)
            {
                context.RebootRequired = true;
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

    private async Task<Result<Unit>> AcquirePayloadIfNeededAsync(PlanAction action, CancellationToken ct)
    {
        var package = action.Package;

        // Only acquire if the package has a download URL
        if (string.IsNullOrEmpty(package.DownloadUrl))
            return Unit.Value;

        // Check cache first
        if (_cache is not null)
        {
            var fileName = Path.GetFileName(package.SourcePath);
            if (_cache.IsCached(Guid.Empty, package, fileName))
                return Unit.Value;
        }

        // Download required
        if (_downloader is null)
            return Result<Unit>.Failure(ErrorKind.DownloadError, $"Package {package.Id} requires download but no downloader is available.");

        var targetPath = package.SourcePath;
        var downloadResult = await _downloader.DownloadAsync(
            package.DownloadUrl,
            package.Sha256Hash,
            targetPath,
            ct: ct);

        if (downloadResult.IsFailure)
            return Result<Unit>.Failure(downloadResult.Error);

        return Unit.Value;
    }
}
