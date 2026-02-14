namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Protocol;
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
}
