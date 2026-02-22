using System.Reflection;
using System.Runtime.Versioning;
using FalkForge.Platform.Windows;
using Xunit;

namespace FalkForge.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class NativeMethodsMsiTests
{
    private static readonly Type NativeMethodsType = typeof(WindowsMsiApi).Assembly
        .GetType("FalkForge.Platform.Windows.NativeMethods", throwOnError: true)!;

    [Fact]
    public void MsiInstallProductW_has_correct_signature()
    {
        var method = NativeMethodsType.GetMethod(
            "MsiInstallProductW",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal(typeof(uint), method.ReturnType);
    }

    [Fact]
    public void MsiConfigureProductW_has_correct_signature()
    {
        var method = NativeMethodsType.GetMethod(
            "MsiConfigureProductW",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
        Assert.Equal(typeof(int), parameters[2].ParameterType);
        Assert.Equal(typeof(uint), method.ReturnType);
    }

    [Fact]
    public void MsiSetInternalUI_has_correct_signature()
    {
        var method = NativeMethodsType.GetMethod(
            "MsiSetInternalUI",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(nint), parameters[1].ParameterType);
        Assert.Equal(typeof(int), method.ReturnType);
    }

    [Theory]
    [InlineData("INSTALLLEVEL_DEFAULT", 0)]
    [InlineData("INSTALLSTATE_ABSENT", 2)]
    [InlineData("INSTALLUILEVEL_NONE", 2)]
    public void Int_constants_have_expected_values(string fieldName, int expected)
    {
        var field = NativeMethodsType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Equal(expected, (int)field!.GetValue(null)!);
    }

    [Theory]
    [InlineData("ERROR_SUCCESS", 0u)]
    [InlineData("ERROR_SUCCESS_REBOOT_REQUIRED", 3010u)]
    public void Uint_constants_have_expected_values(string fieldName, uint expected)
    {
        var field = NativeMethodsType.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Equal(expected, (uint)field!.GetValue(null)!);
    }
}
