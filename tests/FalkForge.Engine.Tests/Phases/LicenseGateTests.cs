namespace FalkForge.Engine.Tests.Phases;

using FalkForge.Engine.Phases;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class LicenseGateTests
{
    private static EngineContext CreateContext(string? licenseFile = null, bool silentMode = false)
    {
        var mockEnv = new MockEnvironment()
            .SetFolderPath(Environment.SpecialFolder.LocalApplicationData, @"C:\Users\Test\AppData\Local")
            .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");

        return new EngineContext
        {
            Manifest = new InstallerManifest
            {
                Name = "TestApp",
                Manufacturer = "TestCo",
                Version = "1.0.0",
                BundleId = Guid.NewGuid(),
                UpgradeCode = Guid.NewGuid(),
                Scope = InstallScope.PerUser,
                Packages = [TestManifestFactory.CreateMsiPackage()],
                LicenseFile = licenseFile
            },
            Platform = new MockPlatformServices(environment: mockEnv),
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            SilentMode = silentMode
        };
    }

    [Fact]
    public async Task LicenseRequired_UserAccepts_ProceedsToPlan()
    {
        var gate = new LicenseGate();
        var context = CreateContext(licenseFile: "license.rtf");
        var licenseContent = "End User License Agreement text here.";

        // Simulate user accepting
        var result = await gate.CheckAsync(
            context,
            licenseContent,
            responseOverride: LicenseAction.Accepted,
            ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task LicenseRequired_UserDeclines_Aborts()
    {
        var gate = new LicenseGate();
        var context = CreateContext(licenseFile: "license.rtf");
        var licenseContent = "End User License Agreement text here.";

        var result = await gate.CheckAsync(
            context,
            licenseContent,
            responseOverride: LicenseAction.Declined,
            ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public async Task LicenseRequired_SilentMode_AutoAccepts()
    {
        var gate = new LicenseGate();
        var context = CreateContext(licenseFile: "license.rtf", silentMode: true);
        var licenseContent = "End User License Agreement text here.";

        var result = await gate.CheckAsync(
            context,
            licenseContent,
            responseOverride: null,
            ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task NoLicense_SkipsGate()
    {
        var gate = new LicenseGate();
        var context = CreateContext(licenseFile: null);

        var result = await gate.CheckAsync(
            context,
            licenseContent: null,
            responseOverride: null,
            ct: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }
}
