using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace FalkForge.Compiler.Msix.Interop;

[ComImport, Guid("5842a140-ff9f-4166-8f5c-62f5b7b0c781")]
internal class AppxFactory { }

[ComImport, Guid("beb94909-e451-438b-b5a7-d79e767b75d8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxFactory
{
    void _VtblGap_CreatePackageReader();

    IAppxPackageWriter CreatePackageWriter(
        IStream outputStream,
        [In] ref APPX_PACKAGE_SETTINGS settings);
}

[ComImport, Guid("9099e33b-246f-41e4-881a-008eb613f858")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxPackageWriter
{
    void AddPayloadFile(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        [MarshalAs(UnmanagedType.LPWStr)] string contentType,
        APPX_COMPRESSION_OPTION compressionOption,
        IStream inputStream);

    void Close(IStream manifest);
}

[StructLayout(LayoutKind.Sequential)]
internal struct APPX_PACKAGE_SETTINGS
{
    [MarshalAs(UnmanagedType.Bool)]
    public bool ForceZip32;

    public IntPtr HashMethod;
}

internal enum APPX_COMPRESSION_OPTION
{
    None = 0,
    Normal = 1,
    Maximum = 2,
    Fast = 3,
    SuperFast = 4,
}

[ComImport, Guid("378e0446-5384-43b7-8877-e7dbdd883446")]
internal class AppxBundleFactory { }

[ComImport, Guid("bba65c6f-4355-4346-bd50-5d82a09cf3d8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxBundleFactory
{
    void _VtblGap_CreateBundleReader();

    IAppxBundleWriter CreateBundleWriter(
        IStream outputStream,
        ulong bundleVersion);
}

[ComImport, Guid("cd965f2d-b7b5-4d4b-bb56-978686a19ad1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxBundleWriter
{
    void AddPayloadPackage(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        IStream packageStream);

    void Close();
}
