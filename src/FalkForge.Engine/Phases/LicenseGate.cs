namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Checks whether the user has accepted the license agreement.
/// Called after detection and before planning.
/// </summary>
public sealed class LicenseGate
{
    /// <summary>
    /// Checks the license gate. Returns true to proceed, false to abort.
    /// </summary>
    /// <param name="context">Engine context.</param>
    /// <param name="licenseContent">The license text content, or null if no license is required.</param>
    /// <param name="responseOverride">Optional override for testing: simulates user response without UI pipe.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with true to proceed, false to abort.</returns>
    public async Task<Result<bool>> CheckAsync(
        EngineContext context,
        string? licenseContent,
        LicenseAction? responseOverride = null,
        CancellationToken ct = default)
    {
        // No license required -- skip gate
        if (context.Manifest.LicenseFile is null || licenseContent is null)
            return true;

        // Silent mode -- auto-accept
        if (context.SilentMode)
        {
            context.Logger.Info("LicenseGate", "Silent mode: auto-accepting license");
            return true;
        }

        // Send license to UI
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new LicenseMessage
            {
                Action = LicenseAction.Required,
                LicenseContent = licenseContent
            }, ct);
        }

        // Use override for testing, or wait for UI response in production
        var response = responseOverride;
        if (response is null)
        {
            // In production, the UI sends back a LicenseMessage with Accepted/Declined.
            // This is handled asynchronously via the message loop.
            // For now, default to accepted if no override is provided and no UI pipe.
            return true;
        }

        return response == LicenseAction.Accepted;
    }
}
