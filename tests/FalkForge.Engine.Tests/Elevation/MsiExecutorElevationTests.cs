namespace FalkForge.Engine.Tests.Elevation;

using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Integrity;
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
                // 64-char hex SHA-256 (of an empty input, verified with `sha256sum`), realistic
                // enough that a test asserting the wire-format shape (hex, 64 chars) is meaningful.
                Sha256Hash = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
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

        // Verify payload contains the source path, args, the caller-asserted hash, the package id, and the
        // signed manifest the elevated companion verifies before installing. No manifest accessor is wired
        // here, so the manifest field is empty — the companion then fails closed.
        using var stream = new MemoryStream(mockClient.LastPayload);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        var msiPath = reader.ReadString();
        var additionalArgs = reader.ReadString();
        var declaredHash = reader.ReadString();
        var packageId = reader.ReadString();
        var manifestJson = reader.ReadString();

        Assert.Equal(@"C:\packages\TestApp.msi", msiPath);
        Assert.Equal("", additionalArgs); // No additional properties
        Assert.Equal(action.Package.Sha256Hash, declaredHash);
        Assert.Equal("TestMsi", packageId);
        Assert.Equal("", manifestJson);
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_CarriesSignedManifest_CompanionCanReadItBack()
    {
        // Round-trip: the engine serializes the session manifest into the MsiInstall payload with the shared
        // Protocol source-generated context, and the companion deserializes it back with the SAME context to
        // hand to the integrity gate. Prove the manifest (and its signed envelope) survives the wire.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var action = CreateMsiAction(PlanActionType.Install, @"C:\packages\TestApp.msi");
        var files = new[]
        {
            new ManifestFileEntry { Name = action.PackageId, Sha256 = action.Package.Sha256Hash }
        };
        var envelope = IntegrityEnvelopeCodec.Serialize(IntegrityEnvelopeCodec.Sign(files, key));
        var manifest = new InstallerManifest
        {
            Name = "App",
            Manufacturer = "Mfg",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = [action.Package],
            ManifestSignature = envelope
        };

        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(
            () => mockClient, static () => null, static () => null, () => manifest);

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        reader.ReadString(); // msiPath
        reader.ReadString(); // additionalArgs
        reader.ReadString(); // declared hash
        var packageId = reader.ReadString();
        var manifestJson = reader.ReadString();

        Assert.Equal(action.PackageId, packageId);
        var roundTripped = JsonSerializer.Deserialize(
            manifestJson, BundleTrustJsonContext.Default.InstallerManifest);
        Assert.NotNull(roundTripped);
        Assert.Equal(envelope, roundTripped.ManifestSignature);
        var package = Assert.Single(roundTripped.Packages);
        Assert.Equal(action.PackageId, package.Id);
        Assert.Equal(action.Package.Sha256Hash, package.Sha256Hash);
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

        // The uninstall wire is now versioned: a magic sentinel, the product code, then the signed manifest.
        // The companion refuses any payload that does not start with the sentinel (no fallback to the old
        // bare-product-code format), so the engine must emit it.
        using var stream = new MemoryStream(mockClient.LastPayload);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        var magic = reader.ReadInt32();
        var productCode = reader.ReadString();

        Assert.Equal(0x4655_4E31, magic);
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

        // Verify payload contains additional args and the declared hash
        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        var msiPath = reader.ReadString();
        var additionalArgs = reader.ReadString();
        var declaredHash = reader.ReadString();
        var packageId = reader.ReadString();
        reader.ReadString(); // manifest (empty — no accessor)

        Assert.Equal(@"C:\packages\TestApp.msi", msiPath);
        Assert.Contains("INSTALLFOLDER=", additionalArgs);
        Assert.Contains("ADDLOCAL=", additionalArgs);
        Assert.Equal(action.Package.Sha256Hash, declaredHash);
        Assert.Equal("TestMsi", packageId);
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_InstallWithSecret_AppendsSecretBlockAndKeepsItOffArgs()
    {
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        using var secret = SensitiveBytes.FromPlaintext("s3cr3t P@ss;&|"u8);
        var action = CreateMsiAction(PlanActionType.Install, @"C:\packages\TestApp.msi");
        action.SecureProperties = new Dictionary<string, SensitiveBytes>(StringComparer.OrdinalIgnoreCase)
        {
            ["SQLPASSWORD"] = secret
        };

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        reader.ReadString(); // msiPath
        var additionalArgs = reader.ReadString();
        reader.ReadString(); // declared hash
        reader.ReadString(); // package id
        reader.ReadString(); // manifest

        // The secret is not on the command-line args.
        Assert.DoesNotContain("SQLPASSWORD", additionalArgs);
        Assert.DoesNotContain("s3cr3t", additionalArgs);

        // The per-package transform block is always present; none declared here, so count 0.
        Assert.Equal(0, reader.ReadInt32());

        // It rides the trailing secret block instead.
        var count = reader.ReadInt32();
        Assert.Equal(1, count);
        Assert.Equal("SQLPASSWORD", reader.ReadString());
        var length = reader.ReadInt32();
        var bytes = reader.ReadBytes(length);
        Assert.Equal("s3cr3t P@ss;&|", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_ForwardsResolvedTransformPaths()
    {
        // The engine forwards the (transformId, resolved path) pairs the ApplyStep
        // resolved under the payload root, on the transform block that sits between the manifest and the
        // optional secret block. The companion re-binds each to its signed hash and association.
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(PlanActionType.Install, @"C:\packages\TestApp.msi");
        action.ResolvedTransformPaths =
        [
            new ResolvedTransform("TestMsi.Lang.de", @"C:\cache\bundle\TestMsi.Lang.de"),
            new ResolvedTransform("TestMsi.Lang.fr", @"C:\cache\bundle\TestMsi.Lang.fr")
        ];

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        reader.ReadString(); // msiPath
        reader.ReadString(); // additionalArgs
        reader.ReadString(); // declared hash
        reader.ReadString(); // package id
        reader.ReadString(); // manifest

        var count = reader.ReadInt32();
        Assert.Equal(2, count);
        Assert.Equal("TestMsi.Lang.de", reader.ReadString());
        Assert.Equal(@"C:\cache\bundle\TestMsi.Lang.de", reader.ReadString());
        Assert.Equal("TestMsi.Lang.fr", reader.ReadString());
        Assert.Equal(@"C:\cache\bundle\TestMsi.Lang.fr", reader.ReadString());
        // No secret block for this install — the stream ends after the transform block.
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task ExecuteAsync_WithElevationClient_NoSecret_WritesNoSecretBlock()
    {
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(PlanActionType.Install, @"C:\packages\TestApp.msi");

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        using var stream = new MemoryStream(mockClient.LastPayload!);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
        reader.ReadString(); // msiPath
        reader.ReadString(); // additionalArgs
        reader.ReadString(); // declared hash
        reader.ReadString(); // package id
        reader.ReadString(); // manifest
        // The per-package transform block is always present; none declared here, so count 0.
        Assert.Equal(0, reader.ReadInt32());
        // No trailing secret block for a non-secret install — the stream ends after the transform block.
        Assert.Equal(stream.Length, stream.Position);
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
        var magic = reader.ReadInt32();
        var uninstallTarget = reader.ReadString();

        // Falls back to SourcePath when ProductCode is not set (behind the versioned wire's magic sentinel).
        Assert.Equal(0x4655_4E31, magic);
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

    [Theory]
    [InlineData('"')]
    [InlineData('&')]
    [InlineData('|')]
    [InlineData(';')]
    [InlineData('>')]
    [InlineData('<')]
    public async Task ElevatedExecution_ProhibitedCharInPropertyValue_ReturnsSecurityError(char prohibited)
    {
        // ProhibitedValueChars covers all 6 injection-relevant chars.
        // Validation runs before the gateway call, so the mock must never be invoked.
        var mockClient = new MockElevationClient();
        var executor = new MsiExecutor(() => mockClient);
        var action = CreateMsiAction(
            PlanActionType.Install,
            properties: new Dictionary<string, string>
            {
                ["MYPROP"] = $"safe{prohibited}injected"
            });

        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsFailure, $"Expected failure for prohibited char '{prohibited}'");
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        // Gateway must not be called — the check fires before elevation dispatch
        Assert.Equal(0, mockClient.CallCount);
    }

    [Fact]
    public async Task ElevatedExecution_ResolvedSecretContainsProhibitedChar_ReturnsSecurityError()
    {
        // When a property value is a [VarName] reference that resolves via the
        // VariableStore to a secret whose plaintext contains a prohibited character,
        // ValidateAndBuildPropertyArgs must still catch it and return SecurityError.
        var mockClient = new MockElevationClient();
        var variableStore = new FalkForge.Engine.Variables.VariableStore();
        Result<int> result;
        try
        {
            variableStore.SetSecret("DB_PASSWORD", "safe;injected"); // ';' is prohibited

#pragma warning disable IDISP011 // false-positive: executor is fully consumed before variableStore is disposed in finally
            var executor = new MsiExecutor(() => mockClient, () => variableStore);
#pragma warning restore IDISP011
            var action = CreateMsiAction(
                PlanActionType.Install,
                properties: new Dictionary<string, string>
                {
                    ["DBPWD"] = "[DB_PASSWORD]" // will be resolved to "safe;injected"
                });

            result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));
        }
        finally
        {
            variableStore.Dispose();
        }

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        // Validation must have fired before the gateway received anything
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

    public Task<Result<byte[]>> SendCommandAsync(string commandName, byte[] payload, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        CallCount++;
        LastCommandName = commandName;
        // Copy the payload: a real transport reads the bytes before returning, and the executor zeroes the
        // original buffer afterward (it may carry secret plaintext). Capturing the reference would then
        // observe a zeroed array.
        LastPayload = (byte[])payload.Clone();
        return Task.FromResult(ResultToReturn);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
