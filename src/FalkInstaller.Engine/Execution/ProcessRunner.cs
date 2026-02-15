namespace FalkInstaller.Engine.Execution;

using System.Diagnostics;

public sealed class ProcessRunner : IProcessRunner
{
    public Task<int> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        return RunAsync(fileName, arguments, onProcessStarted: null, ct);
    }

    public async Task<int> RunAsync(string fileName, string arguments, Action<int>? onProcessStarted, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        onProcessStarted?.Invoke(process.Id);
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
