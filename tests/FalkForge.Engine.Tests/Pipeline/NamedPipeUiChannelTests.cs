namespace FalkForge.Engine.Tests.Pipeline;

using System.Threading.Channels;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;
using Xunit;

/// <summary>
/// Unit tests for NamedPipeUiChannel focusing on the PipelineEvent-to-EngineMessage
/// translation and the EngineMessage-to-UiRequest translation logic.
/// Tests use the internal event-routing methods directly (InternalsVisibleTo) rather
/// than a live pipe, so no actual named pipe is needed.
/// RED: fails until NamedPipeUiChannel exists.
/// </summary>
public sealed class NamedPipeUiChannelTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Interface assignability
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NamedPipeUiChannel_Implements_IUiChannel()
    {
        var ch = NamedPipeUiChannel.CreateNullChannel();
        Assert.IsAssignableFrom<IUiChannel>(ch);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PipelineEvent → EngineMessage translation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TranslateEvent_PhaseChanged_Returns_PhaseChangedMessage()
    {
        var msg = NamedPipeUiChannel.TranslateEvent(new PipelineEvent.PhaseChanged(EnginePhase.Applying));
        var typed = Assert.IsType<PhaseChangedMessage>(msg);
        Assert.Equal(EnginePhase.Applying, typed.Phase);
    }

    [Fact]
    public void TranslateEvent_Progress_Returns_ProgressMessage()
    {
        var msg = NamedPipeUiChannel.TranslateEvent(new PipelineEvent.Progress(42, "Installing pkg"));
        var typed = Assert.IsType<ProgressMessage>(msg);
        Assert.Equal(42, typed.Progress.Current);
        Assert.Equal("Installing pkg", typed.Progress.CurrentPackage);
    }

    [Fact]
    public void TranslateEvent_Log_Returns_LogMessage()
    {
        var msg = NamedPipeUiChannel.TranslateEvent(new PipelineEvent.Log(LogLevel.Warning, "watch out"));
        var typed = Assert.IsType<LogMessage>(msg);
        Assert.Equal(LogLevel.Warning, typed.Level);
        Assert.Equal("watch out", typed.Text);
    }

    [Fact]
    public void TranslateEvent_Failed_Returns_ErrorMessage()
    {
        var msg = NamedPipeUiChannel.TranslateEvent(
            new PipelineEvent.Failed(ErrorKind.EngineError, "boom"));
        var typed = Assert.IsType<ErrorMessage>(msg);
        Assert.Equal(ErrorKind.EngineError, typed.Kind);
        Assert.Equal("boom", typed.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // EngineMessage → UiRequest translation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TranslateMessage_Cancel_Returns_Cancel_Request()
    {
        var request = NamedPipeUiChannel.TranslateMessage(new CancelMessage(), null, null);
        Assert.IsType<UiRequest.Cancel>(request);
    }

    [Fact]
    public void TranslateMessage_Shutdown_Returns_Shutdown_Request()
    {
        var request = NamedPipeUiChannel.TranslateMessage(new ShutdownRequestMessage(), null, null);
        Assert.IsType<UiRequest.Shutdown>(request);
    }

    [Fact]
    public void TranslateMessage_RequestDetect_Returns_Detect_Request()
    {
        var request = NamedPipeUiChannel.TranslateMessage(new RequestDetectMessage(), null, null);
        Assert.IsType<UiRequest.Detect>(request);
    }

    [Fact]
    public void TranslateMessage_RequestApply_Returns_Apply_Request()
    {
        var request = NamedPipeUiChannel.TranslateMessage(new RequestApplyMessage(), null, null);
        Assert.IsType<UiRequest.Apply>(request);
    }

    [Fact]
    public void TranslateMessage_LaunchUpdate_Returns_LaunchUpdate_Request()
    {
        var request = NamedPipeUiChannel.TranslateMessage(new LaunchUpdateMessage(), null, null);
        Assert.IsType<UiRequest.LaunchUpdate>(request);
    }

    [Fact]
    public void TranslateMessage_RequestPlan_Bundles_Previously_Set_Directory_And_Features()
    {
        var features = new Dictionary<string, bool> { ["Feature1"] = true };
        var installDir = @"C:\MyApp";

        var request = NamedPipeUiChannel.TranslateMessage(
            new RequestPlanMessage { Action = InstallAction.Install },
            installDir,
            features);

        var plan = Assert.IsType<UiRequest.Plan>(request);
        Assert.Equal(InstallAction.Install, plan.Action);
        Assert.Equal(@"C:\MyApp", plan.InstallDirectory);
        Assert.True(plan.FeatureSelections["Feature1"]);
    }

    [Fact]
    public void TranslateMessage_Unknown_Returns_Null()
    {
        // LogMessage is not an inbound request type
        var result = NamedPipeUiChannel.TranslateMessage(
            new LogMessage { Level = LogLevel.Info, Text = "x" }, null, null);
        Assert.Null(result);
    }
}
