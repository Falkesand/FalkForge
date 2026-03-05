namespace FalkForge.Platform.Windows;

/// <summary>
/// Callback signature for MSI external UI handler.
/// Matches the MsiSetExternalUIW callback contract.
/// </summary>
public delegate int MsiExternalUIHandler(nint context, uint messageType, string message);
