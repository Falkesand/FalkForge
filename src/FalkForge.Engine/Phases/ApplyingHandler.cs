namespace FalkForge.Engine.Phases;

using System.Diagnostics;
using FalkForge.Engine.Cache;
using FalkForge.Engine.Download;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.RestartManager;

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

        // Restart Manager: start session, register files, and shut down affected processes
        var rmActive = false;
        var rmShutdownPerformed = false;

        if (context.RestartManagerEnabled && context.RestartManager is not null)
        {
            try
            {
                rmActive = TryStartRestartManagerSession(context, plan);
                if (rmActive)
                {
                    rmShutdownPerformed = TryShutdownAffectedProcesses(context);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // RM is best-effort; do not fail the installation if it errors
                context.Logger.Warning("RestartManager", $"Restart Manager failed: {ex.Message}");
                rmActive = false;
                rmShutdownPerformed = false;
            }
        }

        try
        {
            // If we have segments, use segment-aware execution
            if (plan.Segments.Count > 0)
            {
                return await ExecuteWithSegmentsAsync(context, plan, totalPackages, ct);
            }

            // Fallback: flat execution for backwards compatibility
            return await ExecuteFlatAsync(context, plan, totalPackages, ct);
        }
        finally
        {
            // Restart Manager: restart processes and end session (always in finally)
            if (rmActive && context.RestartManager is not null)
            {
                try
                {
                    if (rmShutdownPerformed)
                    {
                        context.RestartManager.RestartProcesses();
                    }
                }
                finally
                {
                    context.RestartManager.EndSession();
                }
            }
        }
    }

    /// <summary>
    /// Starts a Restart Manager session and registers all package source paths.
    /// Returns true if the session was successfully started and resources registered.
    /// </summary>
    private static bool TryStartRestartManagerSession(EngineContext context, InstallPlan plan)
    {
        var rm = context.RestartManager!;
        var startResult = rm.StartSession();
        if (startResult.IsFailure)
            return false;

        var filePaths = CollectPackageFilePaths(plan);
        if (filePaths.Count == 0)
            return true;

        var registerResult = rm.RegisterResources(filePaths);
        return registerResult.IsSuccess;
    }

    /// <summary>
    /// Queries for affected processes and shuts them down gracefully if any exist.
    /// Returns true if shutdown was performed (processes need to be restarted later).
    /// </summary>
    private static bool TryShutdownAffectedProcesses(EngineContext context)
    {
        var rm = context.RestartManager!;
        var affectedResult = rm.GetAffectedProcesses();
        if (affectedResult.IsFailure)
            return false;

        if (affectedResult.Value.Count == 0)
            return false;

        var shutdownResult = rm.ShutdownProcesses();
        if (shutdownResult.IsFailure)
        {
            context.RebootPending = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Collects all package source file paths from the install plan for RM registration.
    /// </summary>
    private static List<string> CollectPackageFilePaths(InstallPlan plan)
    {
        var filePaths = new List<string>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            if (!string.IsNullOrEmpty(action.Package.SourcePath))
            {
                filePaths.Add(action.Package.SourcePath);
            }
        }

        return filePaths;
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

            // Write segment boundary to journal
            context.Journal?.BeginSegment(segment.BoundaryId);

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

                var lastProgressTime = Stopwatch.GetTimestamp();
                var progress = new Progress<int>(percent =>
                {
                    var now = Stopwatch.GetTimestamp();
                    if (Stopwatch.GetElapsedTime(lastProgressTime, now).TotalMilliseconds < 100 && percent < 100)
                        return;

                    lastProgressTime = now;

                    if (context.UiPipe is { IsConnected: true })
                    {
                        _ = context.UiPipe.SendAsync(new ProgressMessage
                        {
                            Progress = new InstallProgress(
                                globalActionIndex + 1,
                                totalPackages,
                                action.PackageId,
                                percent)
                        }, ct);
                    }
                });

                var result = await _executor.ExecuteAsync(action, ct, progress);
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

                // Record successful installation in rollback journal
                WriteInstallJournalEntry(context.Journal, action);

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

            var lastProgressTime = Stopwatch.GetTimestamp();
            var progress = new Progress<int>(percent =>
            {
                var now = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(lastProgressTime, now).TotalMilliseconds < 100 && percent < 100)
                    return;

                lastProgressTime = now;

                if (context.UiPipe is { IsConnected: true })
                {
                    _ = context.UiPipe.SendAsync(new ProgressMessage
                    {
                        Progress = new InstallProgress(
                            i + 1,
                            totalPackages,
                            action.PackageId,
                            percent)
                    }, ct);
                }
            });

            var result = await _executor.ExecuteAsync(action, ct, progress);
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

            // Record successful installation in rollback journal
            WriteInstallJournalEntry(context.Journal, action);

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

    private static void WriteInstallJournalEntry(RollbackJournal? journal, PlanAction action)
    {
        if (journal is null)
            return;

        var package = action.Package;
        var entryType = package.Type switch
        {
            PackageType.MsiPackage => JournalEntryType.MsiInstalled,
            PackageType.ExePackage => JournalEntryType.ExeInstalled,
            _ => JournalEntryType.PackageInstalled
        };

        var productCode = package.Properties.GetValueOrDefault("ProductCode");
        var uninstallCommand = package.Properties.GetValueOrDefault("UninstallCommand");

        journal.WriteEntry(new JournalEntry
        {
            EntryType = entryType,
            Description = $"Installed {package.Type} package '{package.Id}'",
            PackageId = package.Id,
            PackageType = package.Type.ToString(),
            ProductCode = productCode,
            UninstallCommand = uninstallCommand,
            CachePath = package.SourcePath
        });
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
