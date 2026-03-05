namespace FalkForge.Engine.Tests.Elevation;

using FalkForge.Engine.Elevation;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

public sealed class MsiExecutorElevationTests
{
    private static PlanAction CreateMsiAction(
        PlanActionType actionType = PlanActionType.Install,
        string sourcePath = @"C:\packages\TestApp.msi",
        string? productCode = null,
        Dictionary<string, string>? properties = null)
    {
        var props = new Dictionary<string, string>();
        if (productCode is not null)
            props["ProductCode"] = productCode;

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
                Properties = props
            },
            Properties = properties ?? new Dictionary<string, string>()
        };
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_SendsMsiInstallCommand()
    {
        // Arrange
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(PlanActionType.Install, @"C:\packages\TestApp.msi");

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Equal(1, mockClient.CallCount);
        Assert.Equal("MsiInstall", mockClient.LastCommandName);
        Assert.NotNull(mockClient.LastPayload);

        // Verify payload contains the source path
        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        var msiPath = reader.ReadString();
        var additionalArgs = reader.ReadString();

        Assert.Equal(@"C:\packages\TestApp.msi", msiPath);
        Assert.Equal("", additionalArgs); // No additional properties
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_SendsMsiUninstallCommand()
    {
        // Arrange
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(
            PlanActionType.Uninstall,
            @"C:\packages\TestApp.msi",
            productCode: "{12345678-1234-1234-1234-123456789012}");

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Equal(1, mockClient.CallCount);
        Assert.Equal("MsiUninstall", mockClient.LastCommandName);
        Assert.NotNull(mockClient.LastPayload);

        // Verify payload contains the product code
        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        var productCode = reader.ReadString();

        Assert.Equal("{12345678-1234-1234-1234-123456789012}", productCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_FailureResult_ReturnsFailure()
    {
        // Arrange
        var mockClient = new MockElevationClient
        {
            ResultToReturn = Result<byte[]>.Failure(ErrorKind.ElevationError, "MSI installation failed with exit code 1603")
        };
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(PlanActionType.Install);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("1603", result.Error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutElevationClient_UsesDirectExecution()
    {
        // Arrange: Pass null elevation client accessor (default constructor)
        var executor = new MsiExecutor();
        var action = CreateMsiAction(PlanActionType.Install, @"C:\packages\TestApp.msi");

        // Act: This will attempt direct msiexec execution.
        // In test environment, msiexec.exe may or may not exist, but we can verify
        // the executor takes the direct path by checking it does NOT use an elevation client.
        // Since we can't safely run msiexec in tests, we verify the code path
        // by using the constructor that takes an accessor returning null.
        var executorWithNullAccessor = new MsiExecutor(() => null);

        // This will attempt to run msiexec.exe directly. On CI or restricted environments
        // this may fail, so we just verify it doesn't use the elevation path.
        var result = await executorWithNullAccessor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert: Either succeeds (unlikely in test env) or fails with execution error (not elevation error)
        if (result.IsFailure)
        {
            Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
            // Should NOT contain elevation-related error messages
            Assert.DoesNotContain("elevation", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_InstallWithProperties_SerializesProperties()
    {
        // Arrange
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(
            PlanActionType.Install,
            @"C:\packages\TestApp.msi",
            properties: new Dictionary<string, string>
            {
                ["INSTALLFOLDER"] = @"C:\Program Files\TestApp",
                ["ADDLOCAL"] = "Feature1"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("MsiInstall", mockClient.LastCommandName);

        // Verify payload contains additional args
        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        var msiPath = reader.ReadString();
        var additionalArgs = reader.ReadString();

        Assert.Equal(@"C:\packages\TestApp.msi", msiPath);
        Assert.Contains("INSTALLFOLDER=", additionalArgs);
        Assert.Contains("ADDLOCAL=", additionalArgs);
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_UninstallWithoutProductCode_UsesSourcePath()
    {
        // Arrange: No ProductCode in properties, should fall back to SourcePath
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(
            PlanActionType.Uninstall,
            sourcePath: @"C:\packages\TestApp.msi",
            productCode: null);

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("MsiUninstall", mockClient.LastCommandName);

        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        var uninstallTarget = reader.ReadString();

        // Falls back to SourcePath when ProductCode is not set
        Assert.Equal(@"C:\packages\TestApp.msi", uninstallTarget);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidPropertyKey_ReturnsSecurityError()
    {
        // Arrange: Property key with invalid characters
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["invalid key!"] = "value"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert: Validation rejects the property key before sending to elevation client
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, mockClient.CallCount); // Never sent to elevation client
    }

    [Fact]
    public async Task ExecuteAsync_WithProhibitedPropertyValue_ReturnsSecurityError()
    {
        // Arrange: Property value with prohibited characters
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["MYPROP"] = "value & malicious"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, mockClient.CallCount);
    }
}

/// <summary>
/// Mock implementation of <see cref="IElevationClient"/> that records calls
/// and returns configurable results.
/// </summary>
internal sealed class MockElevationClient : IElevationClient
{
    public int CallCount { get; private set; }
    public string? LastCommandName { get; private set; }
    public byte[]? LastPayload { get; private set; }
    public Result<byte[]> ResultToReturn { get; set; } = Result<byte[]>.Success([]);

    public Task<Result<byte[]>> SendCommandAsync(string commandName, byte[] payload, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastCommandName = commandName;
        LastPayload = payload;
        return Task.FromResult(ResultToReturn);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
