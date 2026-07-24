namespace FalkForge.Engine.Bootstrap;

using System.Runtime.Versioning;
using FalkForge.Engine.Bootstrap.Native;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;
using FalkForge.Platform.Windows;

// ── Concrete adapters wrapping the static helpers for production DI wiring ────────────────────
// These thin wrappers are the ONLY production implementations of the seam interfaces; tests use
// fakes. All adapters are internal — they are only instantiated in BootstrapperRunner.RunAsync.

/// <summary>
/// Production <see cref="IPreUIPrerequisiteDetector"/> that delegates to
/// <see cref="PreUIPrerequisiteDetector"/> with the live Windows registry and file system.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class DefaultPreUIPrerequisiteDetector : IPreUIPrerequisiteDetector
{
    private readonly PreUIPrerequisiteDetector _inner =
        new(new WindowsRegistry(), WindowsFileSystemProvider.Instance);

    public List<PreUIPackageInfo> FindMissing(IReadOnlyList<PreUIPackageInfo> declared)
        => _inner.FindMissing(declared);
}

/// <summary>
/// Production <see cref="IElevationProbe"/> that delegates to <see cref="ElevationProbe"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DefaultElevationProbe : IElevationProbe
{
    public bool IsElevated() => ElevationProbe.IsElevated();
}

/// <summary>
/// Production <see cref="IElevatedSelfRelauncher"/> that delegates to
/// <see cref="ElevatedSelfRelauncher"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DefaultElevatedSelfRelauncher : IElevatedSelfRelauncher
{
    public int Relaunch(string executablePath, string cacheDir, IReadOnlyList<string>? forwarded = null)
        => ElevatedSelfRelauncher.Relaunch(executablePath, cacheDir, forwarded);
}

/// <summary>
/// Production <see cref="IProgressSinkFactory"/> that creates a <see cref="TaskDialogProgressSink"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TaskDialogProgressSinkFactory : IProgressSinkFactory
{
    public IProgressSinkHandle Create() => new TaskDialogProgressSink();
}

/// <summary>
/// Adapter that wraps <see cref="TaskDialogProgress"/> and exposes it as
/// <see cref="IProgressSink"/>. The underlying dialog is shown on the first progress call
/// (Show is called lazily) and closed on Dispose.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TaskDialogProgressSink : IProgressSinkHandle
{
    private readonly TaskDialogProgress _dialog;
    private bool _shown;
    private int _disposed;

    public TaskDialogProgressSink()
    {
        _dialog = new TaskDialogProgress("Installing prerequisites", "Preparing...");
    }

    public void SetMessage(string text)
    {
        EnsureShown();
        _dialog.SetMessage(text);
    }

    public void SetPercent(int percent)
    {
        EnsureShown();
        _dialog.SetPercent(percent);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _dialog.Close();
        _dialog.Dispose();
    }

    private void EnsureShown()
    {
        if (!_shown)
        {
            _shown = true;
            _dialog.Show();
        }
    }
}
