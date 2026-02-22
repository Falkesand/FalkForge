using System.Runtime.Versioning;
using FalkForge.Platform.Windows;
using Xunit;

namespace FalkForge.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsiApiContractTests
{
    [Fact]
    public void WindowsMsiApi_implements_IMsiApi()
    {
        Assert.True(typeof(IMsiApi).IsAssignableFrom(typeof(WindowsMsiApi)));
    }

    [Fact]
    public void WindowsMsiApi_can_be_instantiated()
    {
        var api = new WindowsMsiApi();
        Assert.NotNull(api);
    }

    [Fact]
    public void IMsiApi_has_InstallProduct_method()
    {
        var method = typeof(IMsiApi).GetMethod("InstallProduct");
        Assert.NotNull(method);
        Assert.Equal(typeof(uint), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
    }

    [Fact]
    public void IMsiApi_has_ConfigureProduct_method()
    {
        var method = typeof(IMsiApi).GetMethod("ConfigureProduct");
        Assert.NotNull(method);
        Assert.Equal(typeof(uint), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
        Assert.Equal(typeof(int), parameters[2].ParameterType);
    }

    [Fact]
    public void IMsiApi_has_SetInternalUI_method()
    {
        var method = typeof(IMsiApi).GetMethod("SetInternalUI");
        Assert.NotNull(method);
        Assert.Equal(typeof(int), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(nint), parameters[1].ParameterType);
    }
}
