namespace FalkForge.Engine.Bootstrap;

/// <summary>
/// Receives progress notifications from <see cref="PreUIPrerequisiteInstaller"/>.
/// The <c>TaskDialogProgress</c> class (Phase 3, row 15) will implement this interface
/// to drive the native TaskDialog UI; the wiring happens in rows 20-25.
/// </summary>
public interface IProgressSink
{
    /// <summary>Updates the text message displayed to the user.</summary>
    /// <param name="text">Human-readable status line (e.g., "Installing .NET 10 Desktop Runtime…").</param>
    void SetMessage(string text);

    /// <summary>Updates the overall progress percentage.</summary>
    /// <param name="percent">Value in [0, 100] representing progress across all queued packages.</param>
    void SetPercent(int percent);
}
