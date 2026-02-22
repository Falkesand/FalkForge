using System.IO.Pipes;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Protocol.Transport;

ElevationSecurityLog.Initialize();

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
    Console.Error.WriteLine("Usage: FalkForge.Engine.Elevation --pipe <name> --secret-pipe <name> --parent-pid <pid>");
    ElevationSecurityLog.Shutdown();
    return 1;
}

ElevationSecurityLog.Info("Startup", $"Elevated process started: pipe={pipeName}, parentPid={parentPid}");

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
            Console.Error.WriteLine("Parent closed init pipe before sending full secret");
            ElevationSecurityLog.Shutdown();
            return 1;
        }
        totalRead += read;
    }
}
catch (Exception ex)
{
    ElevationSecurityLog.SecurityEvent("InitPipe", $"Failed to read secret from init pipe: {ex.Message}");
    Console.Error.WriteLine($"Failed to read secret from init pipe: {ex.Message}");
    ElevationSecurityLog.Shutdown();
    return 1;
}

var options = new PipeConnectionOptions
{
    PipeName = pipeName,
    SharedSecret = secret,
    OnSecurityEvent = msg => ElevationSecurityLog.SecurityEvent("Handshake", msg)
};

await using var host = new ElevatedHost(options, parentPid);
var exitCode = await host.RunAsync();

ElevationSecurityLog.Info("Shutdown", $"Elevated process exiting with code {exitCode}");
ElevationSecurityLog.Shutdown();
return exitCode;
