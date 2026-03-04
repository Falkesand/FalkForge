namespace FalkForge.Engine.Phases;

using System.Net.Http;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Download;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;

public sealed class DetectingHandler : IEnginePhaseHandler
{
    private readonly PackageDetector _detector;
    private readonly UpdateChecker? _updateChecker;

    /// <summary>
    /// Optional download delegate injected for testability.
    /// When null the handler creates a <see cref="PayloadDownloader"/> at runtime.
    /// </summary>
    private readonly Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, CancellationToken, Task<Result<string>>>? _downloadDelegate;

    /// <summary>
    /// Optional pre-resolved update result injected for testing without a real HTTP call.
    /// When non-null the update check step is skipped and this value is used directly.
    /// </summary>
    private readonly Result<UpdateCheckResult>? _injectedUpdateResult;

    public DetectingHandler(PackageDetector detector) : this(detector, null)
    {
    }

    internal DetectingHandler(PackageDetector detector, UpdateChecker? updateChecker)
        : this(detector, updateChecker, injectedUpdateResult: null, downloadDelegate: null)
    {
    }

    internal DetectingHandler(
        PackageDetector detector,
        UpdateChecker? updateChecker,
        Result<UpdateCheckResult>? injectedUpdateResult,
        Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, CancellationToken, Task<Result<string>>>? downloadDelegate)
    {
        _detector = detector;
        _updateChecker = updateChecker;
        _injectedUpdateResult = injectedUpdateResult;
        _downloadDelegate = downloadDelegate;
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

        // Detect related bundles (before features, so upgrade migration can use them)
        var relatedResult = _detector.DetectRelatedBundles(context.Manifest);
        if (relatedResult.IsSuccess)
        {
            context.DetectedRelatedBundles = relatedResult.Value;
        }

        // Detect features using registry + MSI fallback + related bundle migration
        var perPackageStates = _detector.DetectPerPackage(context.Manifest);
        var features = FeatureDetector.Detect(
            context.Manifest.Features,
            context.Platform.Registry,
            context.Manifest.BundleId,
            context.Manifest.Scope,
            perPackageStates,
            context.DetectedRelatedBundles);
        context.DetectedFeatures = features;

        // Detect dependency blockers
        context.DependencyBlockers = DependencyDetector.DetectBlockingDependencies(
            context.Manifest.DependencyProviders,
            context.Platform.Registry);

        // Detect unsatisfied dependency providers
        context.UnsatisfiedProviders = DependencyDetector.DetectUnsatisfiedProviders(
            context.Manifest.DependencyRequirements,
            context.Platform.Registry);

        foreach (var unsatisfied in context.UnsatisfiedProviders)
        {
            if (unsatisfied.IsMissing)
            {
                context.Logger.Warning("DependencyCheck",
                    $"Required dependency provider '{unsatisfied.ProviderKey}' is not installed.");
            }
            else
            {
                context.Logger.Warning("DependencyCheck",
                    $"Dependency provider '{unsatisfied.ProviderKey}' version '{unsatisfied.InstalledVersion}' does not satisfy requirements.");
            }
        }

        // Check for updates (non-blocking — failures are logged and ignored)
        if (context.Manifest.UpdateFeed is not null)
        {
            Result<UpdateCheckResult> updateResult;

            if (_injectedUpdateResult.HasValue)
            {
                // Test seam: use the pre-resolved result to avoid real HTTP calls.
                updateResult = _injectedUpdateResult.Value;
            }
            else if (_updateChecker is not null)
            {
                updateResult = await _updateChecker.CheckForUpdateAsync(
                    context.Manifest.UpdateFeed,
                    context.Manifest.BundleId,
                    context.Manifest.Version,
                    ct).ConfigureAwait(false);
            }
            else
            {
                updateResult = Result<UpdateCheckResult>.Failure(ErrorKind.EngineError,
                    "No update checker configured.");
            }

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

        // Start background download for non-NotifyOnly policies when an update is available
        if (context.Manifest.UpdateFeed?.Policy != UpdatePolicy.NotifyOnly
            && context.AvailableUpdate?.Update is { } pendingUpdate)
        {
            var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            context.UpdateDownloadCts = downloadCts;

            var policy = context.Manifest.UpdateFeed!.Policy;
            var allowResume = context.Manifest.UpdateFeed.AllowResumeDownload;
            var cacheDir = Path.Combine(
                context.Platform.Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FalkForge",
                "UpdateCache",
                context.Manifest.BundleId.ToString("D"));

            Func<string, string, string, IProgress<(long BytesReceived, long TotalBytes)>?, bool, CancellationToken, Task<Result<string>>> downloadFn =
                _downloadDelegate ?? new PayloadDownloader(new HttpClient()).DownloadAsync;

            Func<EngineMessage, CancellationToken, Task> sendFn = context.UiPipe is { IsConnected: true } pipe
                ? (msg, token) => pipe.SendAsync(msg, token)
                : static (_, _) => Task.CompletedTask;

            var downloader = new UpdateDownloader(
                downloadFn,
                sendFn,
                context.Logger,
                policy,
                allowResume);

            context.UpdateDownloadTask = downloader.StartAsync(
                pendingUpdate,
                cacheDir,
                downloadCts.Token);
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
