using FalkForge.Decompiler.TableReaders;
using Xunit;

namespace FalkForge.Decompiler.Tests;

public sealed class ServiceTableReaderTests
{
    [Fact]
    public void Read_EmptyTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("ServiceInstall", []);

        var result = ServiceTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_MissingTable_ReturnsEmptyList()
    {
        using var access = new MockMsiTableAccess();

        var result = ServiceTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_ParsesServiceEntry()
    {
        using var access = new MockMsiTableAccess()
            .WithTable("ServiceInstall",
            [
                // ServiceInstall, Name, DisplayName, ServiceType, StartType, ErrorControl,
                // LoadOrderGroup, Dependencies, StartName, Password, Arguments, Component_, Description_
                ["svc_install", "MyService", "My Service", "16", "2", "1", null, null, "LocalSystem", null, null, "comp1", "A test service"]
            ]);

        var result = ServiceTableReader.Read(access);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("MyService", result.Value[0].Name);
        Assert.Equal("My Service", result.Value[0].DisplayName);
        Assert.Equal("A test service", result.Value[0].Description);
        Assert.Equal(ServiceStartMode.Automatic, result.Value[0].StartMode);
        Assert.Equal(ServiceAccount.LocalSystem, result.Value[0].Account);
    }

    [Fact]
    public void MapStartMode_AllValues()
    {
        Assert.Equal(ServiceStartMode.Automatic, ServiceTableReader.MapStartMode(2));
        Assert.Equal(ServiceStartMode.Manual, ServiceTableReader.MapStartMode(3));
        Assert.Equal(ServiceStartMode.Disabled, ServiceTableReader.MapStartMode(4));
    }

    [Fact]
    public void MapServiceAccount_LocalService()
    {
        var (account, userName) = ServiceTableReader.MapServiceAccount("NT AUTHORITY\\LocalService");
        Assert.Equal(ServiceAccount.LocalService, account);
        Assert.Null(userName);
    }

    [Fact]
    public void MapServiceAccount_NetworkService()
    {
        var (account, userName) = ServiceTableReader.MapServiceAccount("NT AUTHORITY\\NetworkService");
        Assert.Equal(ServiceAccount.NetworkService, account);
        Assert.Null(userName);
    }

    [Fact]
    public void MapServiceAccount_CustomUser()
    {
        var (account, userName) = ServiceTableReader.MapServiceAccount("DOMAIN\\user");
        Assert.Equal(ServiceAccount.User, account);
        Assert.Equal("DOMAIN\\user", userName);
    }

    [Fact]
    public void MapServiceAccount_Null_DefaultsToLocalSystem()
    {
        var (account, userName) = ServiceTableReader.MapServiceAccount(null);
        Assert.Equal(ServiceAccount.LocalSystem, account);
        Assert.Null(userName);
    }
}
