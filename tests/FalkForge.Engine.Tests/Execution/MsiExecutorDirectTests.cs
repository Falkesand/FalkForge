namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Engine.Variables;
using Xunit;

public sealed class MsiExecutorDirectTests
{
    private static PlanAction CreateMsiAction(
        PlanActionType actionType = PlanActionType.Install,
        string sourcePath = @"C:\packages\TestApp.msi",
        string? productCode = null,
        Dictionary<string, string>? properties = null)
    {
        var packageProps = new Dictionary<string, string>();
        if (productCode is not null)
            packageProps["ProductCode"] = productCode;

        return new PlanAction
        {
            PackageId = "TestMsi",
            ActionType = actionType,
            Package = new PackageInfo
            {
                Id = "TestMsi",
                Type = PackageType.MsiPackage,
                DisplayName = "Test MSI Package",
                SourcePath = sourcePath,
                Sha256Hash = "AABBCCDD",
                Properties = packageProps
            },
            Properties = properties ?? new Dictionary<string, string>()
        };
    }

    [Fact]
    public async Task DirectExecution_Install_CallsInstallProduct()
    {
        // Arrange
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            @"C:\packages\TestApp.msi",
            properties: new Dictionary<string, string>
            {
                ["INSTALLFOLDER"] = @"C:\MyApp"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, mockApi.InstallProductCallCount);
        Assert.Equal(@"C:\packages\TestApp.msi", mockApi.LastPackagePath);
        Assert.NotNull(mockApi.LastCommandLine);
        Assert.Contains("INSTALLFOLDER=", mockApi.LastCommandLine);
    }

    [Fact]
    public async Task DirectExecution_Install_SetsInternalUIToNone()
    {
        // Arrange
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(PlanActionType.Install);

        // Act
        await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.Equal(1, mockApi.SetInternalUICallCount);
        Assert.Equal(2, mockApi.LastUILevel); // INSTALLUILEVEL_NONE = 2
    }

    [Fact]
    public async Task DirectExecution_Uninstall_CallsConfigureProduct()
    {
        // Arrange
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Uninstall,
            productCode: "{12345678-1234-1234-1234-123456789012}");

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, mockApi.ConfigureProductCallCount);
        Assert.Equal("{12345678-1234-1234-1234-123456789012}", mockApi.LastProductCode);
        Assert.Equal(0, mockApi.LastInstallLevel);  // INSTALLLEVEL_DEFAULT
        Assert.Equal(2, mockApi.LastInstallState);   // INSTALLSTATE_ABSENT
    }

    [Fact]
    public async Task DirectExecution_ReturnsExitCode()
    {
        // Arrange
        var mockApi = new MockMsiApi { InstallProductReturnCode = 1603 };
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(PlanActionType.Install);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1603, result.Value);
    }

    [Fact]
    public async Task DirectExecution_RebootRequired_ReturnsExitCode3010()
    {
        // Arrange
        var mockApi = new MockMsiApi { InstallProductReturnCode = 3010 };
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(PlanActionType.Install);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3010, result.Value);
    }

    [Fact]
    public async Task DirectExecution_NullMsiApi_ReturnsFailure()
    {
        // Arrange
        var executor = new MsiExecutor(() => null, () => null, () => null);
        var action = CreateMsiAction(PlanActionType.Install);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("MSI API not available", result.Error.Message);
    }

    [Fact]
    public async Task DirectExecution_ExceptionFromMsiApi_ReturnsFailure()
    {
        // Arrange
        var mockApi = new MockMsiApi
        {
            ThrowOnInstall = true,
            ThrowMessage = "Access denied to MSI database"
        };
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(PlanActionType.Install);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("Access denied to MSI database", result.Error.Message);
    }

    [Fact]
    public async Task DirectExecution_Repair_CallsInstallProductWithReinstall()
    {
        // Arrange
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(PlanActionType.Repair);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, mockApi.InstallProductCallCount);
        Assert.Equal(@"C:\packages\TestApp.msi", mockApi.LastPackagePath);
        Assert.NotNull(mockApi.LastCommandLine);
        Assert.Contains("REINSTALL=ALL", mockApi.LastCommandLine);
        Assert.Contains("REINSTALLMODE=vomus", mockApi.LastCommandLine);
    }

    [Fact]
    public async Task DirectExecution_InstallNoProperties_PassesNullCommandLine()
    {
        // Arrange
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(PlanActionType.Install);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, mockApi.InstallProductCallCount);
        Assert.Null(mockApi.LastCommandLine);
    }

    [Fact]
    public async Task DirectExecution_UninstallWithoutProductCode_UsesSourcePath()
    {
        // Arrange: No ProductCode in package properties
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Uninstall,
            sourcePath: @"C:\packages\TestApp.msi",
            productCode: null);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert: Falls back to SourcePath
        Assert.True(result.IsSuccess);
        Assert.Equal(1, mockApi.ConfigureProductCallCount);
        Assert.Equal(@"C:\packages\TestApp.msi", mockApi.LastProductCode);
    }

    [Fact]
    public async Task DirectExecution_ElevationClientTakesPrecedence()
    {
        // Arrange: Both elevation client and MSI API provided
        var mockClient = new Elevation.MockElevationClient();
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => mockClient, () => null, () => mockApi);
        var action = CreateMsiAction(PlanActionType.Install);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert: Elevation path was used, not direct MSI API
        Assert.True(result.IsSuccess);
        Assert.Equal(1, mockClient.CallCount);
        Assert.Equal(0, mockApi.InstallProductCallCount);
        Assert.Equal(0, mockApi.SetInternalUICallCount);
    }

    [Fact]
    public async Task DirectExecution_BracketReference_ResolvesSecretFromVariableStore()
    {
        // Arrange
        var variableStore = new VariableStore();
        variableStore.SetSecret("DB_PASSWORD", "s3cret");

        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => variableStore, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["DB_PASSWORD"] = "[DB_PASSWORD]"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(mockApi.LastCommandLine);
        Assert.Contains("s3cret", mockApi.LastCommandLine);
        Assert.DoesNotContain("[DB_PASSWORD]", mockApi.LastCommandLine);
    }
}
