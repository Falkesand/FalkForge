namespace FalkForge.Compiler.Msi.Signing;

using System.Diagnostics;

internal static class SigilDetector
{
    private static bool? _isAvailable;
    private static string? _version;

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

            _version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            _isAvailable = process.ExitCode == 0;
        }
        catch
        {
            _isAvailable = false;
        }

        return _isAvailable.Value;
    }

    internal static string? GetVersion()
    {
        IsAvailable();
        return _version;
    }

    internal static void Reset()
    {
        _isAvailable = null;
        _version = null;
    }
}
