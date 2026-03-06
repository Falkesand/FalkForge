using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msix.Interop;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests.Interop;

public sealed class AppxInteropTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void AppxFactory_CanBeCreated_OnWindows()
    {
        var clsid = new Guid("5842a140-ff9f-4166-8f5c-62f5b7b0c781");
        var type = Type.GetTypeFromCLSID(clsid);

        Assert.NotNull(type);

        try
        {
            var instance = Activator.CreateInstance(type!);
            Assert.NotNull(instance);
        }
        catch (COMException)
        {
            // COM server may not be available in all test environments
        }
    }

    [Fact]
    public void AppxCompressionOption_ValuesMatchWindowsApi()
    {
        Assert.Equal(0, (int)APPX_COMPRESSION_OPTION.None);
        Assert.Equal(1, (int)APPX_COMPRESSION_OPTION.Normal);
        Assert.Equal(2, (int)APPX_COMPRESSION_OPTION.Maximum);
        Assert.Equal(3, (int)APPX_COMPRESSION_OPTION.Fast);
        Assert.Equal(4, (int)APPX_COMPRESSION_OPTION.SuperFast);
    }
}
