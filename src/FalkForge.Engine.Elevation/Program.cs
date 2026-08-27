using System.IO.Pipes;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Protocol.Transport;

ElevationSecurityLog.Initialize();

// Harden the secure transform staging directory, then clear anything a previously killed companion left
// there (a crash misses the per-install cleanup). Hardening runs FIRST so the sweep never operates on a
// directory an attacker may still have pre-planted or redirected. Best-effort — never blocks startup.
SecureTransformStaging.HardenAndSweep();

string? pipeName = null;
string? secretPipeName = null;
int parentPid = 0;

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--pipe":
            pipeName = args[++i];
            break;
        case "--secret-pipe":
            secretPipeName = args[++i];
            break;
        case "--parent-pid":
            if (!int.TryParse(args[++i], out parentPid))
                parentPid = 0;
            break;
    }
}

if (pipeName is null || secretPipeName is null || parentPid == 0)
{
    ElevationSecurityLog.Error("Startup", "Invalid arguments: missing --pipe, --secret-pipe, or --parent-pid");
    await Console.Error.WriteLineAsync("Usage: FalkForge.Engine.Elevation --pipe <name> --secret-pipe <name> --parent-pid <pid>");
    ElevationSecurityLog.Shutdown();
    return 1;
}

// SECURITY: never log the main pipe name to a same-user-readable location — it would let a
// same-user attacker learn the name and race to squat it. Log only the non-sensitive parent PID.
ElevationSecurityLog.Info("Startup", $"Elevated process started: parentPid={parentPid}");

// Read the 32-byte HMAC secret from the one-shot init pipe (never passed via CLI args)
var secret = new byte[32];
try
{
    using var initPipe = new NamedPipeClientStream(".", secretPipeName, PipeDirection.In);
    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await initPipe.ConnectAsync(connectCts.Token);

    var totalRead = 0;
    while (totalRead < 32)
    {
        var read = await initPipe.ReadAsync(secret.AsMemory(totalRead));
        if (read == 0)
        {
            ElevationSecurityLog.SecurityEvent("InitPipe", "Parent closed init pipe before sending full secret");
            await Console.Error.WriteLineAsync("Parent closed init pipe before sending full secret");
            ElevationSecurityLog.Shutdown();
            return 1;
        }
        totalRead += read;
    }
}
catch (Exception ex)
{
    ElevationSecurityLog.SecurityEvent("InitPipe", $"Failed to read secret from init pipe: {ex.Message}");
    await Console.Error.WriteLineAsync($"Failed to read secret from init pipe: {ex.Message}");
    ElevationSecurityLog.Shutdown();
    return 1;
}

var options = new PipeConnectionOptions
{
    PipeName = pipeName,
    SharedSecret = secret,
    // Server-PID binding: the pipe server MUST be our expected parent engine (known out-of-band
    // via --parent-pid, never trusted from the wire). Defeats a same-user rogue server squat.
    ExpectedServerProcessId = parentPid,
    OnSecurityEvent = msg => ElevationSecurityLog.SecurityEvent("Handshake", msg)
};

int exitCode;
try
{
    await using var host = new ElevatedHost(options, parentPid);
    exitCode = await host.RunAsync();
}
catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
{
    // Top level. An exception here used to end the process with nothing written anywhere.
    ElevationSecurityLog.Error("Startup", $"Elevated process failed: {ex.GetType().Name}: {ex.Message}");
    await Console.Error.WriteLineAsync($"Elevated process failed: {ex.Message}");
    exitCode = 1;
}

ElevationSecurityLog.Info("Shutdown", $"Elevated process exiting with code {exitCode}");
ElevationSecurityLog.Shutdown();
return exitCode;
