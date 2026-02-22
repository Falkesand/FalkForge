using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class MsiInstallCommandTests : IDisposable
{
    private readonly string _tempMsiPath;
    private readonly MockMsiApi _mockMsiApi = new();
    private readonly MsiInstallCommand _command;

    public MsiInstallCommandTests()
    {
        _tempMsiPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.msi");
        File.WriteAllBytes(_tempMsiPath, [0x00]);
        _command = new MsiInstallCommand(_mockMsiApi);
    }

    public void Dispose()
    {
        if (File.Exists(_tempMsiPath))
            File.Delete(_tempMsiPath);
    }

    [Fact]
    public void Execute_Install_CallsInstallProduct()
    {
        var payload = BuildPayload(_tempMsiPath, "PROP=VALUE");

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
        Assert.Equal(_tempMsiPath, _mockMsiApi.LastPackagePath);
        Assert.Equal("PROP=VALUE", _mockMsiApi.LastCommandLine);
    }

    [Fact]
    public void Execute_Install_SetsUIToSilent()
    {
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        _command.Execute(payload);

        Assert.Equal(1, _mockMsiApi.SetInternalUICallCount);
        Assert.Equal(2, _mockMsiApi.LastUILevel); // INSTALLUILEVEL_NONE
    }

    [Fact]
    public void Execute_Install_ReturnsSuccess()
    {
        _mockMsiApi.InstallProductReturnCode = 0;
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        var exitCode = ReadExitCode(result.Value);
        Assert.Equal(0u, exitCode);
    }

    [Fact]
    public void Execute_Install_RebootRequired_ReturnsSuccess()
    {
        _mockMsiApi.InstallProductReturnCode = 3010;
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        var exitCode = ReadExitCode(result.Value);
        Assert.Equal(3010u, exitCode);
    }

    [Fact]
    public void Execute_Install_Failure_ReturnsError()
    {
        _mockMsiApi.InstallProductReturnCode = 1603; // Fatal error during installation
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("1603", result.Error.Message);
    }

    [Fact]
    public void Execute_Install_EmptyAdditionalArgs_PassesNullCommandLine()
    {
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        _command.Execute(payload);

        Assert.Null(_mockMsiApi.LastCommandLine);
    }

    [Theory]
    [InlineData(@"\\server\share\evil.msi")]
    [InlineData(@"\\.\pipe\evil")]
    [InlineData(@"\\?\UNC\server\share\evil.msi")]
    public void Execute_RejectsUncPaths(string path)
    {
        var payload = BuildPayload(path, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("UNC", result.Error.Message);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Theory]
    [InlineData("PROP=VALUE & whoami")]
    [InlineData("PROP=VALUE | net user")]
    [InlineData("PROP=VALUE ; rm -rf /")]
    [InlineData("PROP=VALUE > output.txt")]
    [InlineData("PROP=VALUE < input.txt")]
    [InlineData("PROP=VALUE `id`")]
    [InlineData("PROP=VALUE $(whoami)")]
    public void Execute_RejectsShellMetachars(string additionalArgs)
    {
        var payload = BuildPayload(_tempMsiPath, additionalArgs);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("metacharacter", result.Error.Message);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_FileNotFound_ReturnsError()
    {
        var payload = BuildPayload(@"C:\nonexistent\fake.msi", string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("not found", result.Error.Message);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_ExceptionInMsiApi_ReturnsError()
    {
        _mockMsiApi.ThrowOnInstall = true;
        _mockMsiApi.ThrowMessage = "Access denied by mock";
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("Access denied by mock", result.Error.Message);
    }

    private static byte[] BuildPayload(string msiPath, string additionalArgs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(msiPath);
        writer.Write(additionalArgs);
        writer.Flush();
        return stream.ToArray();
    }

    private static uint ReadExitCode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        return reader.ReadUInt32();
    }
}
