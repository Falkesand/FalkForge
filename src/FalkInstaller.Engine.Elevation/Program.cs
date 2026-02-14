using FalkInstaller.Engine.Elevation;
using FalkInstaller.Engine.Protocol.Transport;

string? pipeName = null;
string? secret = null;
int parentPid = 0;

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--pipe":
            pipeName = args[++i];
            break;
        case "--secret":
            secret = args[++i];
            break;
        case "--parent-pid":
            if (!int.TryParse(args[++i], out parentPid))
                parentPid = 0;
            break;
    }
}

if (pipeName is null || secret is null || parentPid == 0)
{
    Console.Error.WriteLine("Usage: FalkInstaller.Engine.Elevation --pipe <name> --secret <base64> --parent-pid <pid>");
    return 1;
}

var options = new PipeConnectionOptions
{
    PipeName = pipeName,
    SharedSecret = Convert.FromBase64String(secret)
};

await using var host = new ElevatedHost(options, parentPid);
return await host.RunAsync();
