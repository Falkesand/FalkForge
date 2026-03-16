using System.Diagnostics;

namespace FalkForge.Compiler.Bundle.Compilation;

internal static class BundleSigilDetector
{
    private static bool? _isAvailable;

    internal static bool IsAvailable()
    {
        if (_isAvailable.HasValue)
            return _isAvailable.Value;

        try
        {
            var psi = new ProcessStartInfo("sigil", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                _isAvailable = false;
                return false;
            }

            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            _isAvailable = process.ExitCode == 0;
        }
        catch
        {
            _isAvailable = false;
        }

        return _isAvailable.Value;
    }

    internal static void Reset()
    {
        _isAvailable = null;
    }
}
