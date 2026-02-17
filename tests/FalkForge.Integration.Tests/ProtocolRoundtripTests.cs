using System.Security.Cryptography;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

namespace FalkForge.Integration.Tests;

public sealed class ProtocolRoundtripTests
{
    private static PipeConnectionOptions CreateOptions()
    {
        return new PipeConnectionOptions
        {
            PipeName = $"integration-{Guid.NewGuid()}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(10)
        };
    }

    private static async Task<(PipeServer Server, PipeClient Client)> EstablishConnectionAsync(
        PipeConnectionOptions options,
        Func<EngineMessage, Task> serverHandler,
        Func<EngineMessage, Task> clientHandler)
    {
        var server = new PipeServer(options, serverHandler);
        var serverTask = server.StartAsync();

        var client = new PipeClient(options, clientHandler);
        var clientResult = await client.ConnectAsync();
        var serverResult = await serverTask;

        Assert.True(serverResult.IsSuccess, $"Server connect failed: {(serverResult.IsFailure ? serverResult.Error.Message : "")}");
        Assert.True(clientResult.IsSuccess, $"Client connect failed: {(clientResult.IsFailure ? clientResult.Error.Message : "")}");

        return (server, client);
    }

    [Fact]
    public async Task FullProtocolRoundtrip_AllMessageTypes_ClientToServer()
    {
        var options = CreateOptions();
        var receivedMessages = new List<EngineMessage>();
        var allReceived = new TaskCompletionSource();
        const int expectedCount = 18;

        var (server, client) = await EstablishConnectionAsync(
            options,
            msg =>
            {
                lock (receivedMessages)
                {
                    receivedMessages.Add(msg);
                    if (receivedMessages.Count == expectedCount)
                        allReceived.TrySetResult();
                }
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);

        await using (server)
        await using (client)
        {
            uint seq = 0;

            // Send every message type from client to server
            var messages = CreateAllMessageTypes(ref seq);
            foreach (var message in messages)
            {
                var result = await client.SendAsync(message);
                Assert.True(result.IsSuccess, $"Failed to send {message.GetType().Name}: {(result.IsFailure ? result.Error.Message : "")}");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() => allReceived.TrySetCanceled());
            await allReceived.Task;

            Assert.Equal(expectedCount, receivedMessages.Count);
            VerifyAllMessageTypes(receivedMessages);
        }
    }

    [Fact]
    public async Task FullProtocolRoundtrip_AllMessageTypes_ServerToClient()
    {
        var options = CreateOptions();
        var receivedMessages = new List<EngineMessage>();
        var allReceived = new TaskCompletionSource();
        const int expectedCount = 18;

        var (server, client) = await EstablishConnectionAsync(
            options,
            _ => Task.CompletedTask,
            msg =>
            {
                lock (receivedMessages)
                {
                    receivedMessages.Add(msg);
                    if (receivedMessages.Count == expectedCount)
                        allReceived.TrySetResult();
                }
                return Task.CompletedTask;
            });

        await using (server)
        await using (client)
        {
            uint seq = 0;
            var messages = CreateAllMessageTypes(ref seq);
            foreach (var message in messages)
            {
                var result = await server.SendAsync(message);
                Assert.True(result.IsSuccess, $"Failed to send {message.GetType().Name}: {(result.IsFailure ? result.Error.Message : "")}");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() => allReceived.TrySetCanceled());
            await allReceived.Task;

            Assert.Equal(expectedCount, receivedMessages.Count);
            VerifyAllMessageTypes(receivedMessages);
        }
    }

    [Fact]
    public async Task FullProtocolRoundtrip_BidirectionalExchange()
    {
        var options = CreateOptions();
        var serverReceived = new TaskCompletionSource<EngineMessage>();
        var clientReceived = new TaskCompletionSource<EngineMessage>();

        var (server, client) = await EstablishConnectionAsync(
            options,
            msg => { serverReceived.TrySetResult(msg); return Task.CompletedTask; },
            msg => { clientReceived.TrySetResult(msg); return Task.CompletedTask; });

        await using (server)
        await using (client)
        {
            // Client sends RequestDetect to server
            await client.SendAsync(new RequestDetectMessage { SequenceId = 100 });

            // Server sends DetectComplete back to client
            await server.SendAsync(new DetectCompleteMessage
            {
                SequenceId = 101,
                State = InstallState.Installed,
                CurrentVersion = "2.0.0",
                Features =
                [
                    new FeatureState("core", "Core Feature", "Core components", true, false, true, 1024000),
                    new FeatureState("extras", "Extra Plugins", "Optional plugins", false, false, false, 512000)
                ]
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() =>
            {
                serverReceived.TrySetCanceled();
                clientReceived.TrySetCanceled();
            });

            var srvMsg = await serverReceived.Task;
            var cliMsg = await clientReceived.Task;

            var reqDetect = Assert.IsType<RequestDetectMessage>(srvMsg);
            Assert.Equal(100u, reqDetect.SequenceId);

            var detectComplete = Assert.IsType<DetectCompleteMessage>(cliMsg);
            Assert.Equal(101u, detectComplete.SequenceId);
            Assert.Equal(InstallState.Installed, detectComplete.State);
            Assert.Equal("2.0.0", detectComplete.CurrentVersion);
            Assert.Equal(2, detectComplete.Features.Length);
            Assert.Equal("core", detectComplete.Features[0].FeatureId);
            Assert.True(detectComplete.Features[0].IsSelected);
            Assert.Equal("extras", detectComplete.Features[1].FeatureId);
            Assert.False(detectComplete.Features[1].IsSelected);
        }
    }

    [Fact]
    public async Task FullProtocolRoundtrip_ElevationMessages()
    {
        var options = CreateOptions();
        var serverReceived = new TaskCompletionSource<EngineMessage>();
        var clientReceived = new TaskCompletionSource<EngineMessage>();

        var (server, client) = await EstablishConnectionAsync(
            options,
            msg => { serverReceived.TrySetResult(msg); return Task.CompletedTask; },
            msg => { clientReceived.TrySetResult(msg); return Task.CompletedTask; });

        await using (server)
        await using (client)
        {
            var payload = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0xFE };

            // Server sends ElevateExecute to client (simulating engine -> elevated process)
            await server.SendAsync(new ElevateExecuteMessage
            {
                SequenceId = 200,
                CommandName = "FileWrite",
                CommandPayload = payload
            });

            // Client sends ElevateResult back (simulating elevated -> engine)
            var resultPayload = new byte[] { 0xAA, 0xBB };
            await client.SendAsync(new ElevateResultMessage
            {
                SequenceId = 201,
                Success = true,
                ResultPayload = resultPayload
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() =>
            {
                serverReceived.TrySetCanceled();
                clientReceived.TrySetCanceled();
            });

            var elevExec = Assert.IsType<ElevateExecuteMessage>(await clientReceived.Task);
            Assert.Equal(200u, elevExec.SequenceId);
            Assert.Equal("FileWrite", elevExec.CommandName);
            Assert.Equal(payload, elevExec.CommandPayload);

            var elevResult = Assert.IsType<ElevateResultMessage>(await serverReceived.Task);
            Assert.Equal(201u, elevResult.SequenceId);
            Assert.True(elevResult.Success);
            Assert.Equal(resultPayload, elevResult.ResultPayload);
        }
    }

    [Fact]
    public async Task FullProtocolRoundtrip_MultipleSequentialMessages()
    {
        var options = CreateOptions();
        const int messageCount = 20;
        var receivedMessages = new List<EngineMessage>();
        var allReceived = new TaskCompletionSource();

        var (server, client) = await EstablishConnectionAsync(
            options,
            msg =>
            {
                lock (receivedMessages)
                {
                    receivedMessages.Add(msg);
                    if (receivedMessages.Count == messageCount)
                        allReceived.TrySetResult();
                }
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);

        await using (server)
        await using (client)
        {
            for (var i = 0; i < messageCount; i++)
            {
                var result = await client.SendAsync(new ProgressMessage
                {
                    SequenceId = (uint)i,
                    Progress = new InstallProgress(i + 1, messageCount, $"Package_{i}")
                });
                Assert.True(result.IsSuccess);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            cts.Token.Register(() => allReceived.TrySetCanceled());
            await allReceived.Task;

            Assert.Equal(messageCount, receivedMessages.Count);
            for (var i = 0; i < messageCount; i++)
            {
                var msg = Assert.IsType<ProgressMessage>(receivedMessages[i]);
                Assert.Equal((uint)i, msg.SequenceId);
                Assert.Equal(i + 1, msg.Progress.Current);
                Assert.Equal(messageCount, msg.Progress.Total);
                Assert.Equal($"Package_{i}", msg.Progress.CurrentPackage);
            }
        }
    }

    private static List<EngineMessage> CreateAllMessageTypes(ref uint seq)
    {
        return
        [
            new DetectBeginMessage { SequenceId = seq++ },
            new DetectCompleteMessage
            {
                SequenceId = seq++,
                State = InstallState.Installed,
                CurrentVersion = "1.2.3",
                Features = [new FeatureState("feat1", "Feature One", null, true, false, false, 4096)]
            },
            new PlanBeginMessage { SequenceId = seq++, Action = InstallAction.Install },
            new PlanCompleteMessage
            {
                SequenceId = seq++,
                TotalDiskSpaceRequired = 1024000,
                PackageIds = ["pkg-1", "pkg-2"]
            },
            new ApplyBeginMessage { SequenceId = seq++, TotalPackages = 3 },
            new ApplyCompleteMessage { SequenceId = seq++, ExitCode = 0 },
            new ProgressMessage
            {
                SequenceId = seq++,
                Progress = new InstallProgress(1, 3, "TestPackage")
            },
            new ErrorMessage { SequenceId = seq++, Message = "Test error", Kind = ErrorKind.ExecutionError },
            new PhaseChangedMessage { SequenceId = seq++, Phase = EnginePhase.Applying },
            new CancelMessage { SequenceId = seq++ },
            new LogMessage { SequenceId = seq++, Text = "Log entry", Level = LogLevel.Warning },
            new ShutdownRequestMessage { SequenceId = seq++ },
            new ShutdownResponseMessage { SequenceId = seq++, ExitCode = 42 },
            new SetInstallDirectoryMessage { SequenceId = seq++, Directory = @"C:\Program Files\TestApp" },
            new SetFeatureSelectionMessage { SequenceId = seq++, FeatureId = "feat1", IsSelected = true },
            new RequestDetectMessage { SequenceId = seq++ },
            new RequestPlanMessage { SequenceId = seq++, Action = InstallAction.Uninstall },
            new RequestApplyMessage { SequenceId = seq++ }
        ];
    }

    private static void VerifyAllMessageTypes(List<EngineMessage> messages)
    {
        var idx = 0;

        var detectBegin = Assert.IsType<DetectBeginMessage>(messages[idx]);
        Assert.Equal((uint)idx++, detectBegin.SequenceId);

        var detectComplete = Assert.IsType<DetectCompleteMessage>(messages[idx]);
        Assert.Equal((uint)idx++, detectComplete.SequenceId);
        Assert.Equal(InstallState.Installed, detectComplete.State);
        Assert.Equal("1.2.3", detectComplete.CurrentVersion);
        Assert.Single(detectComplete.Features);
        Assert.Equal("feat1", detectComplete.Features[0].FeatureId);

        var planBegin = Assert.IsType<PlanBeginMessage>(messages[idx]);
        Assert.Equal((uint)idx++, planBegin.SequenceId);
        Assert.Equal(InstallAction.Install, planBegin.Action);

        var planComplete = Assert.IsType<PlanCompleteMessage>(messages[idx]);
        Assert.Equal((uint)idx++, planComplete.SequenceId);
        Assert.Equal(1024000L, planComplete.TotalDiskSpaceRequired);
        Assert.Equal(["pkg-1", "pkg-2"], planComplete.PackageIds);

        var applyBegin = Assert.IsType<ApplyBeginMessage>(messages[idx]);
        Assert.Equal((uint)idx++, applyBegin.SequenceId);
        Assert.Equal(3, applyBegin.TotalPackages);

        var applyComplete = Assert.IsType<ApplyCompleteMessage>(messages[idx]);
        Assert.Equal((uint)idx++, applyComplete.SequenceId);
        Assert.Equal(0, applyComplete.ExitCode);
        Assert.Null(applyComplete.ErrorMessage);

        var progress = Assert.IsType<ProgressMessage>(messages[idx]);
        Assert.Equal((uint)idx++, progress.SequenceId);
        Assert.Equal(1, progress.Progress.Current);
        Assert.Equal(3, progress.Progress.Total);
        Assert.Equal("TestPackage", progress.Progress.CurrentPackage);

        var error = Assert.IsType<ErrorMessage>(messages[idx]);
        Assert.Equal((uint)idx++, error.SequenceId);
        Assert.Equal("Test error", error.Message);
        Assert.Equal(ErrorKind.ExecutionError, error.Kind);

        var phaseChanged = Assert.IsType<PhaseChangedMessage>(messages[idx]);
        Assert.Equal((uint)idx++, phaseChanged.SequenceId);
        Assert.Equal(EnginePhase.Applying, phaseChanged.Phase);

        var cancel = Assert.IsType<CancelMessage>(messages[idx]);
        Assert.Equal((uint)idx++, cancel.SequenceId);

        var log = Assert.IsType<LogMessage>(messages[idx]);
        Assert.Equal((uint)idx++, log.SequenceId);
        Assert.Equal("Log entry", log.Text);
        Assert.Equal(LogLevel.Warning, log.Level);

        var shutdownReq = Assert.IsType<ShutdownRequestMessage>(messages[idx]);
        Assert.Equal((uint)idx++, shutdownReq.SequenceId);

        var shutdownResp = Assert.IsType<ShutdownResponseMessage>(messages[idx]);
        Assert.Equal((uint)idx++, shutdownResp.SequenceId);
        Assert.Equal(42, shutdownResp.ExitCode);

        var setDir = Assert.IsType<SetInstallDirectoryMessage>(messages[idx]);
        Assert.Equal((uint)idx++, setDir.SequenceId);
        Assert.Equal(@"C:\Program Files\TestApp", setDir.Directory);

        var setFeature = Assert.IsType<SetFeatureSelectionMessage>(messages[idx]);
        Assert.Equal((uint)idx++, setFeature.SequenceId);
        Assert.Equal("feat1", setFeature.FeatureId);
        Assert.True(setFeature.IsSelected);

        var reqDetect = Assert.IsType<RequestDetectMessage>(messages[idx]);
        Assert.Equal((uint)idx++, reqDetect.SequenceId);

        var reqPlan = Assert.IsType<RequestPlanMessage>(messages[idx]);
        Assert.Equal((uint)idx++, reqPlan.SequenceId);
        Assert.Equal(InstallAction.Uninstall, reqPlan.Action);

        var reqApply = Assert.IsType<RequestApplyMessage>(messages[idx]);
        Assert.Equal((uint)idx, reqApply.SequenceId);
    }
}
