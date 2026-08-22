namespace FalkForge.Engine.Elevation.Tests.Mocks;

using FalkForge.Platform.Windows;

/// <summary>
/// Mock implementation of <see cref="IMsiApi"/> that records calls
/// and returns configurable results for elevation command tests.
/// </summary>
internal sealed class MockMsiApi : IMsiApi
{
    public uint InstallProductReturnCode { get; set; }
    public uint ConfigureProductReturnCode { get; set; }
    public string? LastPackagePath { get; private set; }
    public string? LastCommandLine { get; private set; }
    public string? LastProductCode { get; private set; }
    public int LastInstallLevel { get; private set; } = -1;
    public int LastInstallState { get; private set; } = -1;
    public int SetInternalUICallCount { get; private set; }
    public int LastUILevel { get; private set; } = -1;
    public int InstallProductCallCount { get; private set; }
    public int ConfigureProductCallCount { get; private set; }
    public bool ThrowOnInstall { get; set; }
    public string? ThrowMessage { get; set; }

    /// <summary>
    /// Runs synchronously from inside <see cref="InstallProduct"/>, before it returns, so a test
    /// can observe the file-sharing state the caller is holding at the moment the (mocked) MSI
    /// engine would be reading the file — the same moment a real <c>MsiInstallProductW</c> call
    /// would be reading it.
    /// </summary>
    public Action? OnInstallProductCalled { get; set; }

    public uint InstallProduct(string packagePath, string? commandLine)
    {
        if (ThrowOnInstall)
            throw new InvalidOperationException(ThrowMessage ?? "Mock MSI failure");

        InstallProductCallCount++;
        LastPackagePath = packagePath;
        LastCommandLine = commandLine;
        OnInstallProductCalled?.Invoke();
        return InstallProductReturnCode;
    }

    public uint ConfigureProduct(string productCode, int installLevel, int installState)
    {
        if (ThrowOnInstall)
            throw new InvalidOperationException(ThrowMessage ?? "Mock MSI failure");

        ConfigureProductCallCount++;
        LastProductCode = productCode;
        LastInstallLevel = installLevel;
        LastInstallState = installState;
        return ConfigureProductReturnCode;
    }

    public int SetInternalUI(int uiLevel, nint window)
    {
        SetInternalUICallCount++;
        LastUILevel = uiLevel;
        return 0;
    }

    public nint SetExternalUI(MsiExternalUIHandler? handler, uint messageFilter, nint context)
        => nint.Zero;
}
