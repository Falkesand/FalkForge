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
}
