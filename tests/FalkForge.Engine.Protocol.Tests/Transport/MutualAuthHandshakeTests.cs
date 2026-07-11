using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Transport;

/// <summary>
/// Regression tests for the elevation IPC mutual-authentication fix. The SYSTEM-privileged
/// elevated companion is the pipe CLIENT; before this fix it authenticated the server (engine)
/// but the client never authenticated the server, so a same-user rogue server that squatted the
/// pipe name could drive the companion to execute an attacker-supplied command as SYSTEM.
/// These tests prove the client now refuses any server that cannot prove knowledge of the
/// shared secret, and that it does so BEFORE dispatching a single command.
/// </summary>
public class MutualAuthHandshakeTests
{
    private enum ServerBehavior
    {
        WrongSecret,          // rogue server that does not know the shared secret
        Reflection,           // rogue server that echoes the client's own tag_c back as tag_s
        TruncateBeforeProof,  // server that closes the pipe without ever sending tag_s
        Honest                // legitimate server that knows the secret
    }

    // A hand-rolled server that speaks the mutual handshake wire protocol with a chosen (possibly
    // malicious) strategy, then attempts to push an ElevateExecuteMessage{MsiInstall} attack payload.
    // When <paramref name="holdOpen"/> is provided the server keeps its pipe end open until that
    // task completes, so a test can assert on the client's connected state without racing the
    // server's disposal (dispose breaks the client pipe and flips PipeClient.IsConnected to false).
    private static async Task RunRawServerAsync(
        string pipeName,
        byte[] clientSecret,
        ServerBehavior behavior,
        CancellationToken ct,
        Task? holdOpen = null)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        await server.WaitForConnectionAsync(ct);

        // 1. Server -> client: serverNonce.
        var serverNonce = RandomNumberGenerator.GetBytes(PipeSecurityValidator.NonceSize);
        await server.WriteAsync(serverNonce, ct);
        await server.FlushAsync(ct);

        // 2. Client -> server: clientNonce || tag_c.
        var response = new byte[PipeSecurityValidator.NonceSize + PipeSecurityValidator.HmacSize];
        await ReadExactAsync(server, response, ct);
        var clientNonce = response.AsSpan(0, PipeSecurityValidator.NonceSize).ToArray();
        var clientProof = response.AsSpan(PipeSecurityValidator.NonceSize, PipeSecurityValidator.HmacSize).ToArray();

        // Truncation: bail out before step 3 — never send tag_s. Disposal (await using)
        // closes the pipe, so the client observes EOF before the server proved its identity.
        if (behavior == ServerBehavior.TruncateBeforeProof)
            return;

        // 3. Server -> client: tag_s per the chosen strategy.
        byte[] serverProof = behavior switch
        {
            // Rogue server does not know the secret; use a random secret to fabricate tag_s.
            ServerBehavior.WrongSecret => PipeSecurityValidator.ComputeProof(
                RandomNumberGenerator.GetBytes(32),
                PipeSecurityValidator.ServerProofLabel,
                serverNonce,
                clientNonce),
            // Reflection: echo the client's own proof back, hoping the labels don't differ.
            ServerBehavior.Reflection => clientProof,
            // Honest server proves with the real shared secret and the server label.
            _ => PipeSecurityValidator.ComputeProof(
                clientSecret,
                PipeSecurityValidator.ServerProofLabel,
                serverNonce,
                clientNonce),
        };

        await server.WriteAsync(serverProof, ct);
        await server.FlushAsync(ct);

        // The attack: try to inject a privileged command. A secure client must never dispatch it.
        try
        {
            var attack = new ElevateExecuteMessage
            {
                SequenceId = 1,
                CommandName = "MsiInstall",
                CommandPayload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }
            };
            var payload = MessageSerializer.Serialize(attack);
            var frame = new byte[sizeof(int) + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
            payload.CopyTo(frame, sizeof(int));
            await server.WriteAsync(frame, ct);
            await server.FlushAsync(ct);
        }
        catch (IOException)
        {
            // Expected: a secure client closes the pipe after rejecting the handshake.
        }

        // Keep the server's pipe end open until the test has finished asserting; returning here
        // disposes the pipe (await using), which would asynchronously break the client's pipe.
        if (holdOpen is not null)
            await holdOpen.WaitAsync(ct);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
                throw new EndOfStreamException("Pipe closed before full read");
            total += read;
        }
    }

    [Fact]
    public async Task Client_rejects_server_that_does_not_know_the_secret()
    {
        var pipeName = $"test-{Guid.NewGuid()}";
        var secret = RandomNumberGenerator.GetBytes(32);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var commandDispatched = false;
        var serverTask = RunRawServerAsync(pipeName, secret, ServerBehavior.WrongSecret, cts.Token);

        var options = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = secret,
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };
        await using var client = new PipeClient(options, _ =>
        {
            commandDispatched = true;
            return Task.CompletedTask;
        });

        var result = await client.ConnectAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.HandshakeError, result.Error.Kind);
        Assert.False(client.IsConnected);

        try { await serverTask; } catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException) { }

        // The attack command must NEVER have reached the command handler.
        Assert.False(commandDispatched);
    }

    [Fact]
    public async Task Client_rejects_server_that_reflects_the_client_challenge()
    {
        // Proves domain separation (LABEL_C2S != LABEL_S2C) is load-bearing: a server that
        // simply echoes the client's own tag_c cannot pass as a valid server proof.
        var pipeName = $"test-{Guid.NewGuid()}";
        var secret = RandomNumberGenerator.GetBytes(32);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var commandDispatched = false;
        var serverTask = RunRawServerAsync(pipeName, secret, ServerBehavior.Reflection, cts.Token);

        var options = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = secret,
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };
        await using var client = new PipeClient(options, _ =>
        {
            commandDispatched = true;
            return Task.CompletedTask;
        });

        var result = await client.ConnectAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.HandshakeError, result.Error.Kind);
        Assert.False(client.IsConnected);

        try { await serverTask; } catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException) { }

        Assert.False(commandDispatched);
    }

    [Fact]
    public async Task Client_rejects_server_that_disconnects_before_proving_identity()
    {
        // Truncation: a server that runs the handshake up to receiving clientNonce || tag_c but
        // closes the pipe WITHOUT sending tag_s has never proven knowledge of the secret. The
        // client must treat the truncated handshake as a failure — never as an authenticated
        // connection — and must not dispatch any command.
        var pipeName = $"test-{Guid.NewGuid()}";
        var secret = RandomNumberGenerator.GetBytes(32);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var commandDispatched = false;
        var serverTask = RunRawServerAsync(pipeName, secret, ServerBehavior.TruncateBeforeProof, cts.Token);

        var securityEvents = new List<string>();
        var options = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = secret,
            ConnectionTimeout = TimeSpan.FromSeconds(5),
            OnSecurityEvent = securityEvents.Add
        };
        await using var client = new PipeClient(options, _ =>
        {
            commandDispatched = true;
            return Task.CompletedTask;
        });

        var result = await client.ConnectAsync(cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.HandshakeError, result.Error.Kind);
        Assert.False(client.IsConnected);

        try { await serverTask; } catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException) { }

        // No command may ever reach the handler over an unproven connection.
        Assert.False(commandDispatched);

        // Pin the EXACT branch: the failure must come from the missing tag_s (server never
        // proved its identity), not from the earlier serverNonce-truncation branch.
        Assert.Contains(
            "Server disconnected during HMAC handshake before proving its identity",
            securityEvents);
    }

    [Fact]
    public async Task Client_accepts_honest_server_and_completes_handshake()
    {
        // Control: the exact same harness with an honest server (real secret + server label)
        // must succeed, proving the rejection tests fail for the RIGHT reason.
        var pipeName = $"test-{Guid.NewGuid()}";
        var secret = RandomNumberGenerator.GetBytes(32);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Hold the server's pipe end open until the assertions below have run: if the server
        // returned (and disposed its pipe) first, the client's receive loop would observe the
        // broken pipe and flip IsConnected to false before the assertion — a scheduling race
        // that flaked under full-suite load.
        var assertionsDone = new TaskCompletionSource();
        var serverTask = RunRawServerAsync(pipeName, secret, ServerBehavior.Honest, cts.Token, assertionsDone.Task);

        var options = new PipeConnectionOptions
        {
            PipeName = pipeName,
            SharedSecret = secret,
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };
        await using var client = new PipeClient(options, _ => Task.CompletedTask);

        var result = await client.ConnectAsync(cts.Token);

        try
        {
            Assert.True(result.IsSuccess);
            Assert.True(client.IsConnected);
        }
        finally
        {
            // Release the server even when an assertion throws, so its task never leaks.
            assertionsDone.SetResult();
        }

        try { await serverTask; } catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException) { }
    }

    [Fact]
    public async Task Real_server_and_client_exchange_a_command_after_mutual_handshake()
    {
        // Happy path through the production PipeServer + PipeClient: mutual handshake completes
        // and a command actually flows (dispatch works end-to-end).
        var options = new PipeConnectionOptions
        {
            PipeName = $"test-{Guid.NewGuid()}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };
        var received = new TaskCompletionSource<EngineMessage>();

        await using var server = new PipeServer(options, msg =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        });
        var serverTask = server.StartAsync();

        await using var client = new PipeClient(options, _ => Task.CompletedTask);
        var connectResult = await client.ConnectAsync();
        var serverResult = await serverTask;

        Assert.True(connectResult.IsSuccess);
        Assert.True(serverResult.IsSuccess);

        await client.SendAsync(new CancelMessage { SequenceId = 7 });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        timeout.Token.Register(() => received.TrySetCanceled());
        var msg = await received.Task;

        Assert.IsType<CancelMessage>(msg);
        Assert.Equal(7u, msg.SequenceId);
    }

    [Fact]
    public async Task Client_rejects_server_whose_pid_differs_from_expected_parent()
    {
        // PID binding is Windows-only (GetNamedPipeServerProcessId); the production check is
        // skipped off-Windows, so this test would assert nothing there. Skip explicitly rather
        // than report a false green on a non-Windows runner.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Server-PID binding: the in-process test server is owned by THIS process, so an
        // ExpectedServerProcessId that is not this process must be refused before any command.
        var options = new PipeConnectionOptions
        {
            PipeName = $"test-{Guid.NewGuid()}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        await using var server = new PipeServer(options, _ => Task.CompletedTask);
        var serverTask = server.StartAsync();

        var wrongPid = Environment.ProcessId == int.MaxValue ? 1 : Environment.ProcessId + 1;
        var clientOptions = new PipeConnectionOptions
        {
            PipeName = options.PipeName,
            SharedSecret = options.SharedSecret,
            ConnectionTimeout = TimeSpan.FromSeconds(5),
            ExpectedServerProcessId = wrongPid
        };

        var commandDispatched = false;
        await using var client = new PipeClient(clientOptions, _ =>
        {
            commandDispatched = true;
            return Task.CompletedTask;
        });

        var result = await client.ConnectAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.HandshakeError, result.Error.Kind);
        Assert.False(client.IsConnected);
        Assert.False(commandDispatched);

        try { await serverTask; } catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException) { }
    }

    [Fact]
    public async Task Client_accepts_server_whose_pid_matches_expected_parent()
    {
        // PID binding is Windows-only (GetNamedPipeServerProcessId); skip explicitly off-Windows
        // rather than pass without exercising the check.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        // Positive PID-binding case: the in-process server's owner PID == this process, so a
        // matching ExpectedServerProcessId completes the handshake.
        var options = new PipeConnectionOptions
        {
            PipeName = $"test-{Guid.NewGuid()}",
            SharedSecret = RandomNumberGenerator.GetBytes(32),
            ConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        await using var server = new PipeServer(options, _ => Task.CompletedTask);
        var serverTask = server.StartAsync();

        var clientOptions = new PipeConnectionOptions
        {
            PipeName = options.PipeName,
            SharedSecret = options.SharedSecret,
            ConnectionTimeout = TimeSpan.FromSeconds(5),
            ExpectedServerProcessId = Environment.ProcessId
        };

        await using var client = new PipeClient(clientOptions, _ => Task.CompletedTask);

        var result = await client.ConnectAsync();
        var serverResult = await serverTask;

        Assert.True(result.IsSuccess);
        Assert.True(serverResult.IsSuccess);
        Assert.True(client.IsConnected);
    }
}
