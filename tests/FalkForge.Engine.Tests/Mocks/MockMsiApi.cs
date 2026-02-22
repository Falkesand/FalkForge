namespace FalkForge.Engine.Tests.Mocks;

using FalkForge.Platform.Windows;

/// <summary>
/// Mock implementation of <see cref="IMsiApi"/> that records calls
/// and returns configurable results.
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

    public uint InstallProduct(string packagePath, string? commandLine)
    {
        if (ThrowOnInstall)
            throw new InvalidOperationException(ThrowMessage ?? "Mock MSI failure");

        InstallProductCallCount++;
        LastPackagePath = packagePath;
        LastCommandLine = commandLine;
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
}
