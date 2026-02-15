namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Protocol.Messages;

public sealed class DetectingHandler : IEnginePhaseHandler
{
    private readonly PackageDetector _detector;

    public DetectingHandler(PackageDetector detector)
    {
        _detector = detector;
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
        context.DetectedFeatures = result.Features;

        // Detect related bundles
        var relatedResult = _detector.DetectRelatedBundles(context.Manifest);
        if (relatedResult.IsSuccess)
        {
            context.DetectedRelatedBundles = relatedResult.Value;
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
                Features = result.Features
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
