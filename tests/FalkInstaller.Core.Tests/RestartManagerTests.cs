using FalkInstaller.Builders;
using FalkInstaller.Models;
using FalkInstaller.Testing;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class RestartManagerTests
{
    [Fact]
    public void EnableRestartManager_SetsPropertyOnModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.EnableRestartManager = true;
        });

        Assert.True(package.EnableRestartManager);
    }

    [Fact]
    public void EnableRestartManagerSupport_FluentMethod_SetsProperty()
    {
        var builder = InstallerTestHost.CreateBuilder();
        builder.Name = "App";
        builder.Manufacturer = "Corp";

        var returnedBuilder = builder.EnableRestartManagerSupport();
        var package = builder.Build();

        Assert.Same(builder, returnedBuilder);
        Assert.True(package.EnableRestartManager);
    }
}
