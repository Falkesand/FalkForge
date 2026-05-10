using System.Diagnostics;
using System.IO;

namespace FalkForge.Ui.ViewModels;

/// <summary>
/// Shared helpers for the "Open log" / "Open log folder" buttons surfaced on
/// the Complete and Maintenance pages. Centralised here so both view models
/// behave identically and any future failure page can reuse the same logic.
/// </summary>
internal static class LogPathActions
{
    /// <summary>
    /// Returns true when the supplied path is non-empty and the file exists on disk.
    /// </summary>
    public static bool CanOpen(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
            return false;
        try
        {
            return File.Exists(logPath);
        }
        catch
        {
            // Path might be malformed (invalid characters, too long, etc.).
            // Treat as "cannot open" rather than crashing the UI.
            return false;
        }
    }

    /// <summary>
    /// Opens the log file in the user's default text viewer using shell execute.
    /// Safe no-op when the path is missing or the file does not exist.
    /// </summary>
    public static void OpenLog(string? logPath)
    {
        if (!CanOpen(logPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logPath!,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"LogPathActions.OpenLog failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the containing folder. On Windows, selects the file in Explorer.
    /// On other platforms, opens the directory itself. Safe no-op when the
    /// path is missing or the file does not exist.
    /// </summary>
    public static void OpenLogFolder(string? logPath)
    {
        if (!CanOpen(logPath))
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{logPath}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                var directory = Path.GetDirectoryName(logPath);
                if (string.IsNullOrEmpty(directory))
                    return;
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"LogPathActions.OpenLogFolder failed: {ex.Message}");
        }
    }
}
