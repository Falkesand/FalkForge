// SECURITY: Pipe traffic is authenticated via HMAC-SHA256 handshake but is NOT encrypted.
// An attacker with admin or kernel-level access could read named pipe buffers in transit.
// This is an accepted risk: such an attacker already has the ability to read process memory
// directly, attach a debugger, or inject code — making pipe encryption ineffective as a
// mitigation. The HMAC handshake prevents unauthorized (non-admin) processes from connecting.

namespace FalkForge.Engine.Protocol.Transport;

using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using FalkForge.Engine.Protocol.Messages;

public sealed class PipeClient : PipeTransportBase
{
    // Wire layout of the client's handshake response: clientNonce || tag_c.
    private const int ClientResponseSize =
        PipeSecurityValidator.NonceSize + PipeSecurityValidator.HmacSize;

    public PipeClient(PipeConnectionOptions options, Func<EngineMessage, Task> messageHandler)
        : base(options, messageHandler)
    {
    }

    /// <summary>
    /// Test seam for the pipe-owner identity check. Null in production, where the owner is read
    /// from the connected handle. A test sets it to present a chosen owner SID, or null from the
    /// delegate to simulate a descriptor that cannot be read.
    /// <para>
    /// Typed as a string rather than a <c>SecurityIdentifier</c> on purpose: a
    /// <c>SecurityIdentifier</c>-typed member on this cross-platform class pulls CA1416 onto the
    /// property, while a string keeps the platform annotation confined to
    /// <c>VerifyServerOwnerWindows</c>, which already carries it.
    /// </para>
    /// </summary>
    internal Func<string?>? PipeOwnerSidOverride { get; init; }

    public async Task<Result<Unit>> ConnectAsync(CancellationToken ct = default)
    {
        // On Windows this deliberately does NOT set PipeOptions.CurrentUserOnly. That flag makes
        // .NET compare the pipe's owner SID against WindowsIdentity.GetCurrent().Owner, and Owner
        // becomes BUILTIN\Administrators the moment the process is elevated. The elevated
        // companion is exactly such a process, so the comparison refused it on every run. The
        // check is not dropped: VerifyServerOwner below runs the same comparison against the
        // token's account SID, which elevation does not change, before a single handshake byte is
        // read. On Unix the flag checks the socket file's owner uid, which is the right check
        // there and has no elevation split, so keep it.
        var pipeOptions = OperatingSystem.IsWindows()
            ? PipeOptions.Asynchronous
            : PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

        try
        {
            // Constructed inside the try on purpose. NamedPipeClientStream's constructor validates
            // its arguments and can throw, and this method must not let anything escape.
            _pipe = new NamedPipeClientStream(
                ".",
                _options.PipeName,
                PipeDirection.InOut,
                pipeOptions);

            await ((NamedPipeClientStream)_pipe).ConnectAsync((int)_options.ConnectionTimeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException)
        {
            await DisposePipeAsync();
            return Result<Unit>.Failure(ErrorKind.TransportError, "Connection timed out");
        }
        catch (OperationCanceledException)
        {
            await DisposePipeAsync();
            return Result<Unit>.Failure(ErrorKind.TransportError, "Connection cancelled");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Nothing may escape this method. The elevated companion calls it from a top-level
            // statement with no handler, so an escaping exception ended the process with no log
            // entry, and the engine saw only a broken pipe.
            _options.OnSecurityEvent?.Invoke($"Pipe connect failed: {ex.GetType().Name}: {ex.Message}");
            await DisposePipeAsync();
            return Result<Unit>.Failure(ErrorKind.TransportError, $"Connection failed: {ex.Message}");
        }

        // Peer-identity binding: the pipe must be owned by the account this process runs as.
        // Runs before the PID check and before the handshake, so an unrecognised peer never
        // reaches either. Wrapped like the connect step above: nothing may escape this method,
        // and VerifyServerOwnerWindows already fails closed for every exception it recognises, but
        // this is the outer net for anything it does not.
        Result<Unit> ownerResult;
        try
        {
            ownerResult = VerifyServerOwner();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _options.OnSecurityEvent?.Invoke(
                $"Pipe owner verification failed: {ex.GetType().Name}: {ex.Message}");
            await DisposePipeAsync();
            return Result<Unit>.Failure(
                ErrorKind.HandshakeError, $"Pipe owner verification failed: {ex.Message}");
        }

        if (ownerResult.IsFailure)
        {
            await DisposePipeAsync();
            return Result<Unit>.Failure(ownerResult.Error);
        }

        // Server-PID binding: before exchanging any credential, confirm the pipe we connected
        // to is owned by the expected parent engine PID. Defeats a same-user name-squat where a
        // rogue server pre-created the pipe. Skipped when ExpectedServerProcessId is null.
        var pidResult = VerifyServerProcessId();
        if (pidResult.IsFailure)
        {
            await DisposePipeAsync();
            return Result<Unit>.Failure(pidResult.Error);
        }

        // Perform client-side handshake
        Result<Unit> handshakeResult;
        try
        {
            handshakeResult = await PerformClientHandshakeAsync(ct);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _options.OnSecurityEvent?.Invoke($"Pipe handshake failed: {ex.GetType().Name}: {ex.Message}");
            await DisposePipeAsync();
            return Result<Unit>.Failure(ErrorKind.HandshakeError, $"Handshake failed: {ex.Message}");
        }

        if (handshakeResult.IsFailure)
        {
            await DisposePipeAsync();
            return Result<Unit>.Failure(handshakeResult.Error);
        }

        // Start receive loop
        StartReceiveLoop(ct);

        return Unit.Value;
    }

    private async Task DisposePipeAsync()
    {
        if (_pipe is null)
            return;

        try
        {
            await _pipe.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Disposing an already-broken pipe can itself fail. Nothing useful is left to do and
            // the caller is already being told the connection failed.
        }
        finally
        {
            _pipe = null;
        }
    }

    /// <summary>
    /// Refuses any pipe whose owner is not the account this process runs as. Replaces the check
    /// <c>PipeOptions.CurrentUserOnly</c> used to run, which compared against the token's Owner
    /// SID and therefore refused every elevated peer. Fails closed: a descriptor that cannot be
    /// read is not evidence of a trustworthy peer.
    /// </summary>
    private Result<Unit> VerifyServerOwner()
    {
        if (!OperatingSystem.IsWindows())
            return Unit.Value;

        return VerifyServerOwnerWindows();
    }

    [SupportedOSPlatform("windows")]
    private Result<Unit> VerifyServerOwnerWindows()
    {
        SecurityIdentifier? pipeOwner;
        SecurityIdentifier? account;
        try
        {
            pipeOwner = ReadPipeOwner();
            account = PipeIdentity.CurrentAccountSid();
        }
        catch (InvalidOperationException ex)
        {
            // GetAccessControl() throws this when the pipe disconnected between ConnectAsync
            // succeeding and this read (measured 2026-08-27: "The pipe has been disconnected.").
            // That is an ordinary race, not evidence the peer is untrustworthy, so it is
            // unavailability (TransportError), not a refusal (HandshakeError).
            _options.OnSecurityEvent?.Invoke(
                $"Could not read the pipe's owner, so the server's identity is unproven: {ex.Message}");
            return Result<Unit>.Failure(
                ErrorKind.TransportError, "Pipe disconnected before its owner could be read");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
            or System.Security.SecurityException or PrivilegeNotHeldException
            or NotSupportedException or ArgumentException)
        {
            // NotSupportedException (invalid handle, or no security descriptor on the object) and
            // ArgumentException are the other two GetAccessControl() can throw, alongside the four
            // this filter already covered. None of these is an ordinary race — fail closed.
            _options.OnSecurityEvent?.Invoke(
                $"Could not read the pipe's owner, so the server's identity is unproven: {ex.Message}");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Pipe owner could not be read");
        }

        if (PipeIdentity.IsAcceptableOwner(pipeOwner, account))
            return Unit.Value;

        _options.OnSecurityEvent?.Invoke(
            $"Pipe owner binding failed: pipe owner={pipeOwner?.Value ?? "<unreadable>"} " +
            $"does not match this process's account sid={account?.Value ?? "<unreadable>"}");
        return Result<Unit>.Failure(ErrorKind.HandshakeError, "Pipe owner does not match this account");
    }

    /// <summary>
    /// Reads the pipe owner off the connected handle, or returns the test seam's value when set.
    /// Measured 2026-08-27: every ordinary unelevated Windows token this project tested against
    /// refused to set a foreign owner on a pipe it created, so a test cannot otherwise present a
    /// foreign owner to the client on a developer box or on CI.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private SecurityIdentifier? ReadPipeOwner()
    {
        if (PipeOwnerSidOverride is { } readOverride)
        {
            var value = readOverride();
            return value is null ? null : new SecurityIdentifier(value);
        }

        return _pipe!.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
    }

    private Result<Unit> VerifyServerProcessId()
    {
        if (_options.ExpectedServerProcessId is not { } expectedPid)
            return Unit.Value;

        // The elevation model that uses PID binding is Windows-only; on other platforms the
        // kernel32 import is unavailable, so skip (ExpectedServerProcessId is never set there).
        if (!OperatingSystem.IsWindows())
            return Unit.Value;

        var handle = ((NamedPipeClientStream)_pipe!).SafePipeHandle;
        if (!NativePipeMethods.GetNamedPipeServerProcessId(handle, out var serverPid))
        {
            _options.OnSecurityEvent?.Invoke("Unable to determine pipe server process id for PID binding");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Unable to determine pipe server process id");
        }

        if (serverPid != (uint)expectedPid)
        {
            _options.OnSecurityEvent?.Invoke(
                $"Pipe server PID binding failed: connected server pid={serverPid} does not match expected parent pid={expectedPid}");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Pipe server PID does not match expected parent");
        }

        return Unit.Value;
    }

    // Mutual HMAC handshake (client side): read serverNonce, send clientNonce || tag_c, then
    // verify the server's tag_s BEFORE processing any message. If tag_s does not validate the
    // server does not know the shared secret (or reflected our own challenge) — refuse and never
    // start the receive loop, so no command handler is ever invoked for an unauthenticated server.
    private async Task<Result<Unit>> PerformClientHandshakeAsync(CancellationToken ct)
    {
        // Read serverNonce from server.
        var serverNonce = new byte[PipeSecurityValidator.NonceSize];
        if (!await ReadExactAsync(_pipe!, serverNonce, ct))
        {
            _options.OnSecurityEvent?.Invoke("Server disconnected during HMAC handshake before sending complete nonce");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Server disconnected during handshake");
        }

        // Generate clientNonce and prove knowledge of the secret bound to BOTH nonces.
        var clientNonce = PipeSecurityValidator.GenerateNonce();
        var clientProof = PipeSecurityValidator.ComputeProof(
            _options.SharedSecret,
            PipeSecurityValidator.ClientProofLabel,
            serverNonce,
            clientNonce);

        var response = new byte[ClientResponseSize];
        clientNonce.CopyTo(response, 0);
        clientProof.CopyTo(response, PipeSecurityValidator.NonceSize);
        // S8969 false positive: Sonar's null-forgiving-redundancy check does not model that the null-forgiving
        // operator itself is what narrows _pipe's flow state for the FlushAsync call below — removing it here
        // reintroduces a genuine CS8602 (verified). Keep the operator.
#pragma warning disable S8969
        await _pipe!.WriteAsync(response, ct);
#pragma warning restore S8969
        await _pipe.FlushAsync(ct);

        // Read and verify the server's proof (tag_s) — the mutual-auth step. This is the fix:
        // a rogue server that does not know the secret cannot produce a valid tag_s, and domain
        // separation (LABEL_S2C != LABEL_C2S) stops it reflecting our own tag_c back at us.
        var serverProof = new byte[PipeSecurityValidator.HmacSize];
        if (!await ReadExactAsync(_pipe, serverProof, ct))
        {
            _options.OnSecurityEvent?.Invoke("Server disconnected during HMAC handshake before proving its identity");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Server disconnected during handshake");
        }

        if (!PipeSecurityValidator.ValidateProof(
                _options.SharedSecret,
                PipeSecurityValidator.ServerProofLabel,
                serverNonce,
                clientNonce,
                serverProof))
        {
            _options.OnSecurityEvent?.Invoke("Server HMAC validation failed: server presented invalid credentials during handshake");
            return Result<Unit>.Failure(ErrorKind.HandshakeError, "Server HMAC validation failed");
        }

        return Unit.Value;
    }
}
