using System.Security.Cryptography;
using FalkInstaller.Engine.Protocol.Messages;
using FalkInstaller.Engine.Protocol.Transport;
using Xunit;

namespace FalkInstaller.Engine.Protocol.Tests.Transport;

public class PipeTransportTests
{
    private static PipeConnectionOptions CreateOptions(string? pipeName = null, byte[]? secret = null)
    {
        return new PipeConnectionOptions
        {
            PipeName = pipeName ?? $"test-{Guid.NewGuid()}",
            SharedSecret = secret ?? RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };
    }

    [Fact]
    public async Task Server_and_client_connect_with_valid_handshake()
    {
        var options = CreateOptions();

        await using var server = new PipeServer(options, _ => Task.CompletedTask);
        var serverTask = server.StartAsync();

        await using var client = new PipeClient(options, _ => Task.CompletedTask);
        var clientResult = await client.ConnectAsync();
        var serverResult = await serverTask;

        Assert.True(serverResult.IsSuccess);
        Assert.True(clientResult.IsSuccess);
        Assert.True(server.IsConnected);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task Handshake_fails_with_wrong_secret()
    {
        var pipeName = $"test-{Guid.NewGuid()}";
        var serverOptions = CreateOptions(pipeName, RandomNumberGenerator.GetBytes(32));
        var clientOptions = CreateOptions(pipeName, RandomNumberGenerator.GetBytes(32));

        await using var server = new PipeServer(serverOptions, _ => Task.CompletedTask);
        var serverTask = server.StartAsync();

        await using var client = new PipeClient(clientOptions, _ => Task.CompletedTask);
        await client.ConnectAsync();
        var serverResult = await serverTask;

        Assert.True(serverResult.IsFailure);
        Assert.Equal(ErrorKind.HandshakeError, serverResult.Error.Kind);
    }

    [Fact]
    public async Task Client_sends_message_server_receives_it()
    {
        var options = CreateOptions();
        var received = new TaskCompletionSource<EngineMessage>();

        await using var server = new PipeServer(options, msg =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        });
        var serverTask = server.StartAsync();

        await using var client = new PipeClient(options, _ => Task.CompletedTask);
        await client.ConnectAsync();
        await serverTask;

        var message = new LogMessage
        {
            SequenceId = 1,
            Text = "Hello from client",
            Level = LogLevel.Info
        };
        var sendResult = await client.SendAsync(message);

        Assert.True(sendResult.IsSuccess);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => received.TrySetCanceled());
        var receivedMessage = await received.Task;

        var logMsg = Assert.IsType<LogMessage>(receivedMessage);
        Assert.Equal("Hello from client", logMsg.Text);
        Assert.Equal(LogLevel.Info, logMsg.Level);
        Assert.Equal(1u, logMsg.SequenceId);
    }

    [Fact]
    public async Task Server_sends_message_client_receives_it()
    {
        var options = CreateOptions();
        var received = new TaskCompletionSource<EngineMessage>();

        await using var server = new PipeServer(options, _ => Task.CompletedTask);
        var serverTask = server.StartAsync();

        await using var client = new PipeClient(options, msg =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        });
        await client.ConnectAsync();
        await serverTask;

        var message = new ProgressMessage
        {
            SequenceId = 42,
            Progress = new InstallProgress(5, 10, "TestPackage")
        };
        var sendResult = await server.SendAsync(message);

        Assert.True(sendResult.IsSuccess);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => received.TrySetCanceled());
        var receivedMessage = await received.Task;

        var progressMsg = Assert.IsType<ProgressMessage>(receivedMessage);
        Assert.Equal(42u, progressMsg.SequenceId);
        Assert.Equal(5, progressMsg.Progress.Current);
        Assert.Equal(10, progressMsg.Progress.Total);
        Assert.Equal("TestPackage", progressMsg.Progress.CurrentPackage);
    }

    [Fact]
    public async Task Bidirectional_message_exchange()
    {
        var options = CreateOptions();
        var serverReceived = new TaskCompletionSource<EngineMessage>();
        var clientReceived = new TaskCompletionSource<EngineMessage>();

        await using var server = new PipeServer(options, msg =>
        {
            serverReceived.TrySetResult(msg);
            return Task.CompletedTask;
        });
        var serverTask = server.StartAsync();

        await using var client = new PipeClient(options, msg =>
        {
            clientReceived.TrySetResult(msg);
            return Task.CompletedTask;
        });
        await client.ConnectAsync();
        await serverTask;

        // Client sends to server
        await client.SendAsync(new CancelMessage { SequenceId = 1 });

        // Server sends to client
        await server.SendAsync(new LogMessage
        {
            SequenceId = 2,
            Text = "Acknowledged",
            Level = LogLevel.Info
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() =>
        {
            serverReceived.TrySetCanceled();
            clientReceived.TrySetCanceled();
        });

        var serverMsg = await serverReceived.Task;
        var clientMsg = await clientReceived.Task;

        Assert.IsType<CancelMessage>(serverMsg);
        Assert.Equal(1u, serverMsg.SequenceId);

        var logMsg = Assert.IsType<LogMessage>(clientMsg);
        Assert.Equal("Acknowledged", logMsg.Text);
    }

    [Fact]
    public async Task Send_fails_when_not_connected()
    {
        var options = CreateOptions();
        await using var client = new PipeClient(options, _ => Task.CompletedTask);

        var result = await client.SendAsync(new CancelMessage { SequenceId = 1 });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.TransportError, result.Error.Kind);
    }

    [Fact]
    public async Task Max_message_size_enforcement_on_send()
    {
        var options = new PipeConnectionOptions
        {
            PipeName = $"test-{Guid.NewGuid()}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            MaxMessageSize = 50, // Very small limit
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        await using var server = new PipeServer(options, _ => Task.CompletedTask);
        var serverTask = server.StartAsync();

        await using var client = new PipeClient(options, _ => Task.CompletedTask);
        await client.ConnectAsync();
        await serverTask;

        // LogMessage with a long text will exceed 50 bytes when serialized
        var message = new LogMessage
        {
            SequenceId = 1,
            Text = new string('x', 100),
            Level = LogLevel.Info
        };
        var result = await client.SendAsync(message);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.TransportError, result.Error.Kind);
        Assert.Contains("max size", result.Error.Message);
    }

    [Fact]
    public async Task Multiple_messages_sent_in_sequence()
    {
        var options = CreateOptions();
        var receivedMessages = new List<EngineMessage>();
        var allReceived = new TaskCompletionSource();
        const int messageCount = 5;

        await using var server = new PipeServer(options, msg =>
        {
            lock (receivedMessages)
            {
                receivedMessages.Add(msg);
                if (receivedMessages.Count == messageCount)
                    allReceived.TrySetResult();
            }
            return Task.CompletedTask;
        });
        var serverTask = server.StartAsync();

        await using var client = new PipeClient(options, _ => Task.CompletedTask);
        await client.ConnectAsync();
        await serverTask;

        for (var i = 0; i < messageCount; i++)
        {
            var result = await client.SendAsync(new LogMessage
            {
                SequenceId = (uint)i,
                Text = $"Message {i}",
                Level = LogLevel.Debug
            });
            Assert.True(result.IsSuccess);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => allReceived.TrySetCanceled());
        await allReceived.Task;

        Assert.Equal(messageCount, receivedMessages.Count);
        for (var i = 0; i < messageCount; i++)
        {
            var logMsg = Assert.IsType<LogMessage>(receivedMessages[i]);
            Assert.Equal((uint)i, logMsg.SequenceId);
            Assert.Equal($"Message {i}", logMsg.Text);
        }
    }

    [Fact]
    public async Task Graceful_disconnect_client_disposes()
    {
        var options = CreateOptions();

        await using var server = new PipeServer(options, _ => Task.CompletedTask);
        var serverTask = server.StartAsync();

        var client = new PipeClient(options, _ => Task.CompletedTask);
        await client.ConnectAsync();
        await serverTask;

        Assert.True(server.IsConnected);
        Assert.True(client.IsConnected);

        // Client disconnects
        await client.DisposeAsync();

        // Give the server a moment to detect the disconnect
        await Task.Delay(100);

        // Server should detect disconnect - send should fail
        var result = await server.SendAsync(new CancelMessage { SequenceId = 1 });
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.TransportError, result.Error.Kind);
    }

    [Fact]
    public async Task Server_start_cancelled_returns_failure()
    {
        var options = CreateOptions();

        await using var server = new PipeServer(options, _ => Task.CompletedTask);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await server.StartAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.TransportError, result.Error.Kind);
    }
}
