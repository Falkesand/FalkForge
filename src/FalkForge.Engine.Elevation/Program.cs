using System.IO.Pipes;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Protocol.Transport;

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
    Console.Error.WriteLine("Usage: FalkForge.Engine.Elevation --pipe <name> --secret-pipe <name> --parent-pid <pid>");
    return 1;
}

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
            Console.Error.WriteLine("Parent closed init pipe before sending full secret");
            return 1;
        }
        totalRead += read;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to read secret from init pipe: {ex.Message}");
    return 1;
}

var options = new PipeConnectionOptions
{
    PipeName = pipeName,
    SharedSecret = secret
};

await using var host = new ElevatedHost(options, parentPid);
return await host.RunAsync();
