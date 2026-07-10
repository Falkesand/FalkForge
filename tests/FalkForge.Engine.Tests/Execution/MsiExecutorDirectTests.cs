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
    public async Task DirectExecution_SlipstreamPatchPathWithEmbeddedQuote_ReturnsSecurityError()
    {
        // Slipstream patch paths are joined into a PATCH="..." argument string; an embedded
        // quote would break out of the quoting. Property VALUES are already validated —
        // this is the same defense for the patch-path channel (the elevated MsiInstall
        // parser blocks shell metacharacters in PATCH values; this is the engine-side gate).
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = new PlanAction
        {
            PackageId = "TestMsi",
            ActionType = PlanActionType.Install,
            Package = new PackageInfo
            {
                Id = "TestMsi",
                Type = PackageType.MsiPackage,
                DisplayName = "Test MSI Package",
                SourcePath = @"C:\packages\TestApp.msi",
                Sha256Hash = "AABBCCDD"
            },
            SlipstreamPatchPaths = ["C:\\patches\\evil\" TRANSFORMS=evil.mst .msp"]
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, mockApi.InstallProductCallCount);
    }

    [Fact]
    public async Task DirectExecution_SlipstreamPatchPathWithNewline_ReturnsSecurityError()
    {
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = new PlanAction
        {
            PackageId = "TestMsi",
            ActionType = PlanActionType.Install,
            Package = new PackageInfo
            {
                Id = "TestMsi",
                Type = PackageType.MsiPackage,
                DisplayName = "Test MSI Package",
                SourcePath = @"C:\packages\TestApp.msi",
                Sha256Hash = "AABBCCDD"
            },
            SlipstreamPatchPaths = ["C:\\patches\\a\r\nb.msp"]
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, mockApi.InstallProductCallCount);
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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

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
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(mockApi.LastCommandLine);
        Assert.Contains("s3cret", mockApi.LastCommandLine);
        Assert.DoesNotContain("[DB_PASSWORD]", mockApi.LastCommandLine);
    }

    // ── Property-value injection defense (direct execution path) ──────────────

    [Theory]
    [InlineData('"')]
    [InlineData('&')]
    [InlineData('|')]
    [InlineData(';')]
    [InlineData('>')]
    [InlineData('<')]
    public async Task DirectExecution_ProhibitedCharInPropertyValue_ReturnsSecurityError(char prohibited)
    {
        // Arrange: craft a value that embeds the prohibited character
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["MYAPP_PARAM"] = $"safe{prohibited}injected"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert: validation must block before any MSI API call
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("MYAPP_PARAM", result.Error.Message);
        Assert.Equal(0, mockApi.InstallProductCallCount);
        Assert.Equal(0, mockApi.SetInternalUICallCount);
    }

    [Theory]
    [InlineData('"')]
    [InlineData('&')]
    [InlineData('|')]
    [InlineData(';')]
    [InlineData('>')]
    [InlineData('<')]
    public async Task DirectExecution_ProhibitedCharAtStartOfValue_ReturnsSecurityError(char prohibited)
    {
        // Arrange: prohibited char as first character (boundary position)
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["MYAPP_PARAM"] = $"{prohibited}suffix"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, mockApi.InstallProductCallCount);
    }

    [Theory]
    [InlineData('"')]
    [InlineData('&')]
    [InlineData('|')]
    [InlineData(';')]
    [InlineData('>')]
    [InlineData('<')]
    public async Task DirectExecution_ProhibitedCharAtEndOfValue_ReturnsSecurityError(char prohibited)
    {
        // Arrange: prohibited char as last character (boundary position)
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["MYAPP_PARAM"] = $"prefix{prohibited}"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, mockApi.InstallProductCallCount);
    }

    [Theory]
    [InlineData('"')]
    [InlineData('&')]
    [InlineData('|')]
    [InlineData(';')]
    [InlineData('>')]
    [InlineData('<')]
    public async Task DirectExecution_ResolvedSecretContainsProhibitedChar_ReturnsSecurityError(char prohibited)
    {
        // Arrange: secret stored in variable store contains an injection char after resolution
        var variableStore = new VariableStore();
        variableStore.SetSecret("EVIL_SECRET", $"val{prohibited}ue");

        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => variableStore, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["DB_PWD"] = "[EVIL_SECRET]"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert: injection defense applies to resolved secret values too
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("DB_PWD", result.Error.Message);
        Assert.Equal(0, mockApi.InstallProductCallCount);
    }

    [Fact]
    public async Task DirectExecution_CleanAlphanumericValue_Succeeds()
    {
        // Arrange: a completely clean property value must pass
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["INSTALLFOLDER"] = @"C:\MyApp\1.0"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert: not blocked
        Assert.True(result.IsSuccess);
        Assert.Equal(1, mockApi.InstallProductCallCount);
        Assert.NotNull(mockApi.LastCommandLine);
        Assert.Contains("INSTALLFOLDER=", mockApi.LastCommandLine);
    }

    [Theory]
    [InlineData("lowercase")]
    [InlineData("123PROP")]
    [InlineData("MY-PROP")]
    [InlineData("MY PROP")]
    public async Task DirectExecution_InvalidPropertyKey_ReturnsSecurityError(string invalidKey)
    {
        // Arrange: property keys must match ^[A-Z_][A-Z0-9_.]*$
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                [invalidKey] = "SafeValue"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, mockApi.InstallProductCallCount);
    }

    [Fact]
    public async Task DirectExecution_MultiplePropertiesFirstClean_SecondInjection_ReturnsSecurityError()
    {
        // Arrange: injection in second property must still be caught
        var mockApi = new MockMsiApi();
        var executor = new MsiExecutor(() => null, () => null, () => mockApi);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["CLEAN_PROP"] = "CleanValue",
                ["EVIL_PROP"] = "val&ue"
            });

        // Act
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        // Assert: entire operation rejected even though first property was clean
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("EVIL_PROP", result.Error.Message);
        Assert.Equal(0, mockApi.InstallProductCallCount);
    }
}
