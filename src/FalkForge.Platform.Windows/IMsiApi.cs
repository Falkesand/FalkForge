namespace FalkForge.Platform.Windows;

/// <summary>
/// Abstraction over Windows Installer (msi.dll) product installation APIs.
/// Enables deterministic testing of MSI install/uninstall operations.
/// </summary>
public interface IMsiApi
{
    /// <summary>
    /// Installs or configures a product from a package path.
    /// Wraps MsiInstallProduct.
    /// </summary>
    /// <param name="packagePath">Full path to the MSI package.</param>
    /// <param name="commandLine">Command-line property settings (e.g., "PROPERTY=VALUE"), or null.</param>
    /// <returns>Windows Installer error code. 0 = success, 3010 = success (reboot required).</returns>
    uint InstallProduct(string packagePath, string? commandLine);

    /// <summary>
    /// Installs or uninstalls a product by product code.
    /// Wraps MsiConfigureProduct.
    /// </summary>
    /// <param name="productCode">Product code GUID string.</param>
    /// <param name="installLevel">Installation level (0 = default).</param>
    /// <param name="installState">Desired install state (2 = absent/uninstall).</param>
    /// <returns>Windows Installer error code. 0 = success, 3010 = success (reboot required).</returns>
    uint ConfigureProduct(string productCode, int installLevel, int installState);

    /// <summary>
    /// Sets the UI level for subsequent MSI operations.
    /// Wraps MsiSetInternalUI.
    /// </summary>
    /// <param name="uiLevel">UI level (2 = INSTALLUILEVEL_NONE).</param>
    /// <param name="window">Window handle, or IntPtr.Zero.</param>
    /// <returns>Previous UI level.</returns>
    int SetInternalUI(int uiLevel, nint window);

    /// <summary>
    /// Registers an external UI handler for progress callbacks during MSI operations.
    /// Wraps MsiSetExternalUI.
    /// </summary>
    /// <param name="handler">Callback function, or null to unregister.</param>
    /// <param name="messageFilter">Bitmask of message types to receive.</param>
    /// <param name="context">User-defined context pointer.</param>
    /// <returns>Pointer to the previously registered handler.</returns>
    nint SetExternalUI(MsiExternalUIHandler? handler, uint messageFilter, nint context);
}
