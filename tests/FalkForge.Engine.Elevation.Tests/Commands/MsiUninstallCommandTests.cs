using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class MsiUninstallCommandTests
{
    private const string ValidProductCode = "{12345678-1234-1234-1234-123456789012}";
    private readonly MockMsiApi _mockMsiApi = new();
    private readonly MsiUninstallCommand _command;

    public MsiUninstallCommandTests()
    {
        _command = new MsiUninstallCommand(_mockMsiApi);
    }

    [Fact]
    public void Execute_Uninstall_CallsConfigureProduct()
    {
        var payload = BuildPayload(ValidProductCode);

        _command.Execute(payload);

        Assert.Equal(1, _mockMsiApi.ConfigureProductCallCount);
        Assert.Equal(ValidProductCode, _mockMsiApi.LastProductCode);
        Assert.Equal(0, _mockMsiApi.LastInstallLevel);
        Assert.Equal(2, _mockMsiApi.LastInstallState);
    }

    [Fact]
    public void Execute_Uninstall_SetsUIToSilent()
    {
        var payload = BuildPayload(ValidProductCode);

        _command.Execute(payload);

        Assert.Equal(1, _mockMsiApi.SetInternalUICallCount);
        Assert.Equal(2, _mockMsiApi.LastUILevel);
    }

    [Fact]
    public void Execute_Uninstall_ReturnsSuccess()
    {
        _mockMsiApi.ConfigureProductReturnCode = 0;
        var payload = BuildPayload(ValidProductCode);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Execute_Uninstall_RebootRequired_ReturnsSuccess()
    {
        _mockMsiApi.ConfigureProductReturnCode = 3010;
        var payload = BuildPayload(ValidProductCode);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Execute_Uninstall_Failure_ReturnsError()
    {
        _mockMsiApi.ConfigureProductReturnCode = 1603;
        var payload = BuildPayload(ValidProductCode);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("1603", result.Error.Message);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345678-1234-1234-1234-123456789012")]
    [InlineData("{invalid}")]
    [InlineData("")]
    public void Execute_Uninstall_InvalidGuid_ReturnsError(string invalidProductCode)
    {
        var payload = BuildPayload(invalidProductCode);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("GUID", result.Error.Message);
    }

    private static byte[] BuildPayload(string productCode)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(productCode);
        writer.Flush();
        return stream.ToArray();
    }
}
