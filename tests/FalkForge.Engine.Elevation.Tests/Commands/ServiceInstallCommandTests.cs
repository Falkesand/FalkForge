using FalkForge.Engine.Elevation.Commands;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class ServiceInstallCommandTests
{
    [Fact]
    public void Execute_RejectsPathOutsideTrustedDirectories()
    {
        var command = new ServiceInstallCommand();
        var payload = BuildPayload("MySvc", "My Service", @"C:\Users\attacker\evil.exe");

        var result = command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void Execute_RejectsDisplayNameContainingQuotes()
    {
        var command = new ServiceInstallCommand();
        var payload = BuildPayload("MySvc", "My \"Evil\" Service", @"C:\Program Files\MyApp\svc.exe");

        var result = command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("prohibited shell metacharacters", result.Error.Message);
    }

    [Theory]
    [InlineData("svc\" binPath= \"C:\\evil.exe")]
    [InlineData("svc\"\nbinPath= \"C:\\evil.exe")]
    public void Execute_RejectsServiceNameWithInjectionCharacters(string serviceName)
    {
        var command = new ServiceInstallCommand();
        var payload = BuildPayload(serviceName, "Display", @"C:\Program Files\MyApp\svc.exe");

        var result = command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    private static byte[] BuildPayload(string serviceName, string displayName, string binaryPath)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(serviceName);
        writer.Write(displayName);
        writer.Write(binaryPath);
        return stream.ToArray();
    }
}
