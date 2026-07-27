namespace FalkForge.Engine.Tests.RestartManager;

using System.Reflection;
using System.Runtime.Versioning;
using FalkForge.Engine.RestartManager;
using Xunit;

/// <summary>
/// Guard-clause coverage for <see cref="RestartManagerSession"/> -- the production
/// <see cref="IRestartManager"/> implementation that wraps the real Windows Restart
/// Manager API via P/Invoke.
///
/// Scope is deliberately narrow: only the state-machine guards (disposed, no-active-
/// session, already-active) that short-circuit BEFORE any native call are exercised
/// here. The "already active" guard needs a session that is actually active, which in
/// production only happens after a real <c>RmStartSession</c> P/Invoke succeeds; rather
/// than calling into the real Restart Manager from a unit test, reflection sets the
/// private <c>_sessionActive</c> field directly so the guard branch is reached without
/// touching the native API.
///
/// NOT covered here (needs the real Restart Manager service, left untested per the
/// coverage-audit scope): StartSession's native success path, RegisterResources,
/// GetAffectedProcesses, ShutdownProcesses, and RestartProcesses all beyond their guard
/// checks, since each makes a real P/Invoke call requiring a genuinely active RM session.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RestartManagerSessionTests
{
    [Fact]
    public void StartSession_AfterDispose_ReturnsInvalidOperationFailure()
    {
        var session = new RestartManagerSession();
        session.Dispose();

        var result = session.StartSession();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
        Assert.Contains("disposed", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartSession_WhenSessionAlreadyActive_ReturnsInvalidOperationFailure()
    {
        // WHY: a caller that forgets to pair StartSession with EndSession/Dispose must get
        // a clear, typed rejection rather than silently leaking a second native RM handle.
        // _sessionActive is forced true via reflection so the guard is reached without a
        // real RmStartSession P/Invoke.
        using var session = new RestartManagerSession();
        SetSessionActive(session, true);

        var result = session.StartSession();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
        Assert.Contains("already active", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        // Prevent Dispose from attempting a real RmEndSession against the fake handle.
        SetSessionActive(session, false);
    }

    [Fact]
    public void RegisterResources_NoActiveSession_ReturnsInvalidOperationFailure()
    {
        using var session = new RestartManagerSession();

        var result = session.RegisterResources([@"C:\app\file.dll"]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
        Assert.Contains("StartSession", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAffectedProcesses_NoActiveSession_ReturnsInvalidOperationFailure()
    {
        using var session = new RestartManagerSession();

        var result = session.GetAffectedProcesses();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
        Assert.Contains("StartSession", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShutdownProcesses_NoActiveSession_ReturnsInvalidOperationFailure()
    {
        using var session = new RestartManagerSession();

        var result = session.ShutdownProcesses();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
        Assert.Contains("StartSession", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RestartProcesses_NoActiveSession_ReturnsInvalidOperationFailure()
    {
        using var session = new RestartManagerSession();

        var result = session.RestartProcesses();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.InvalidOperation, result.Error.Kind);
        Assert.Contains("StartSession", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EndSession_NoActiveSession_IsNoOpAndDoesNotThrow()
    {
        using var session = new RestartManagerSession();

        var exception = Record.Exception(session.EndSession);

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_WithNoActiveSession_DoesNotThrow()
    {
        var session = new RestartManagerSession();

        var exception = Record.Exception(session.Dispose);

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var session = new RestartManagerSession();
        session.Dispose();

        var exception = Record.Exception(session.Dispose);

        Assert.Null(exception);
    }

    private static void SetSessionActive(RestartManagerSession session, bool value)
    {
        var field = typeof(RestartManagerSession).GetField("_sessionActive", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RestartManagerSession._sessionActive field not found; guard test needs updating.");
        field.SetValue(session, value);
    }
}
