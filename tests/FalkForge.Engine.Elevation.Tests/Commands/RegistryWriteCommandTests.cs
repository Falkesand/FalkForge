using FalkForge.Engine.Elevation.Commands;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class RegistryWriteCommandTests
{
    private readonly RegistryWriteCommand _command = new();

    [Theory]
    [InlineData(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"SOFTWARE\Microsoft\NET Framework Setup\NDP")]
    [InlineData(@"SOFTWARE\Classes\.exe")]
    [InlineData(@"SOFTWARE\Classes\CLSID\{00000000-0000-0000-0000-000000000000}")]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows")]
    public void Execute_RejectsDeniedSubKeyPrefix(string subKey)
    {
        var payload = BuildPayload("HKLM", subKey, "TestValue", "REG_SZ", "data");

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Theory]
    [InlineData(@"SYSTEM\CurrentControlSet\Services")]
    [InlineData(@"SECURITY\Policy")]
    [InlineData(@"SAM\SAM")]
    public void Execute_RejectsNonSoftwareSubKey(string subKey)
    {
        var payload = BuildPayload("HKLM", subKey, "TestValue", "REG_SZ", "data");

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    private static byte[] BuildPayload(
        string rootKey, string subKey, string valueName, string valueType, string valueData)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(rootKey);
        writer.Write(subKey);
        writer.Write(valueName);
        writer.Write(valueType);
        writer.Write(valueData);
        writer.Flush();
        return stream.ToArray();
    }
}
