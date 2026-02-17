namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Download;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;

public sealed class DetectingHandler : IEnginePhaseHandler
{
    private readonly PackageDetector _detector;
    private readonly UpdateChecker? _updateChecker;

    public DetectingHandler(PackageDetector detector) : this(detector, null)
    {
    }

    internal DetectingHandler(PackageDetector detector, UpdateChecker? updateChecker)
    {
        _detector = detector;
        _updateChecker = updateChecker;
    }

    public EnginePhase Phase => EnginePhase.Detecting;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Notify UI that detection is beginning
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new DetectBeginMessage(), ct);
        }

        var result = _detector.Detect(context.Manifest);

        context.DetectedState = result.State;
        context.DetectedVersion = result.CurrentVersion;

        // Detect features using registry + MSI fallback
        var perPackageStates = _detector.DetectPerPackage(context.Manifest);
        var features = FeatureDetector.Detect(
            context.Manifest.Features,
            context.Platform.Registry,
            context.Manifest.BundleId,
            context.Manifest.Scope,
            perPackageStates);
        context.DetectedFeatures = features;

        // Detect dependency blockers
        context.DependencyBlockers = DependencyDetector.DetectBlockingDependencies(
            context.Manifest.DependencyProviders,
            context.Platform.Registry);

        // Detect related bundles
        var relatedResult = _detector.DetectRelatedBundles(context.Manifest);
        if (relatedResult.IsSuccess)
        {
            context.DetectedRelatedBundles = relatedResult.Value;
        }

        // Check for updates (non-blocking — failures are logged and ignored)
        // NOTE: Currently all update policies behave as NotifyOnly (detect + notify UI).
        // DownloadAndPrompt (download to cache, then prompt) and AutoUpdate (download + auto-launch)
        // will be implemented in a future release when the download-and-launch pipeline is built.
        if (_updateChecker is not null && context.Manifest.UpdateFeed is not null)
        {
            var updateResult = await _updateChecker.CheckForUpdateAsync(
                context.Manifest.UpdateFeed,
                context.Manifest.BundleId,
                context.Manifest.Version,
                ct).ConfigureAwait(false);

            if (updateResult.IsSuccess)
            {
                context.AvailableUpdate = updateResult.Value;
                if (updateResult.Value.Update is not null)
                {
                    context.Logger.Info("UpdateCheck",
                        $"Update available: {updateResult.Value.Update.Version}");
                }
            }
            else
            {
                context.Logger.Warning("UpdateCheck",
                    $"Update check failed: {updateResult.Error.Message}");
            }
        }

        // Notify UI if an update is available
        if (context.AvailableUpdate?.Update is { } update
            && context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new UpdateAvailableMessage
            {
                Version = update.Version,
                ReleaseNotes = update.ReleaseNotes,
                DownloadUrl = update.DownloadUrl,
                LocalPath = null
            }, ct).ConfigureAwait(false);
        }

        // Set per-package variables for condition evaluation
        SetPerPackageVariables(context, result);

        // Notify UI that detection is complete
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new DetectCompleteMessage
            {
                State = result.State,
                CurrentVersion = result.CurrentVersion,
                Features = features
            }, ct);
        }

        return EnginePhase.Planning;
    }

    private static void SetPerPackageVariables(EngineContext context, DetectionResult result)
    {
        foreach (var package in context.Manifest.Packages)
        {
            var productCode = package.Properties.GetValueOrDefault("ProductCode");
            if (productCode is not null)
            {
                context.Variables.Set($"InstalledProductCode_{package.Id}", productCode);
            }

            // If we detected a version and the package has a matching product code, set it
            if (result.CurrentVersion is not null && productCode is not null)
            {
                context.Variables.Set($"InstalledVersion_{package.Id}", result.CurrentVersion);
            }

            // Set the detection state per package
            context.Variables.Set($"DetectedState_{package.Id}",
                result.State == InstallState.NotInstalled ? "0" : "1");
        }
    }
}
