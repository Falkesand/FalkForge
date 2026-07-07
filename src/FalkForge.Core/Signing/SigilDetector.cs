using System.Diagnostics;

namespace FalkForge.Signing;

/// <summary>
/// Detects whether the sigil CLI tool is available on the system PATH.
/// Caches the result after first probe. Shared by MSI and Bundle compilers.
/// </summary>
internal static class SigilDetector
{
    // Lock guards _isAvailable and _version so that concurrent Reset() calls from
    // test isolation code cannot race with IsAvailable()'s check-then-set pattern.
    private static readonly object _lock = new();
    private static bool? _isAvailable;
    private static string? _version;

    internal static bool IsAvailable()
    {
        lock (_lock)
        {
            if (_isAvailable.HasValue)
                return _isAvailable.Value;

            try
            {
#pragma warning disable S4036 // PATH lookup is the documented contract: sigil is a user-installed build-time CLI tool (like git/dotnet)
                var psi = new ProcessStartInfo("sigil", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
#pragma warning restore S4036

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
    }

    internal static string? GetVersion()
    {
        IsAvailable();
        lock (_lock)
        {
            return _version;
        }
    }

    internal static void Reset()
    {
        lock (_lock)
        {
            _isAvailable = null;
            _version = null;
        }
    }
}
