using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Xml.Linq;
using FalkForge.Compiler.Msix.Interop;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace FalkForge.Compiler.Msix.Packaging;

[SupportedOSPlatform("windows")]
internal sealed class AppxPackageWriter : IDisposable
{
    private IAppxPackageWriter? _writer;
    private IStream? _outputStream;
    private bool _disposed;

    private AppxPackageWriter(IAppxPackageWriter writer, IStream outputStream)
    {
        _writer = writer;
        _outputStream = outputStream;
    }

    public static Result<string> CreatePackage(
        string outputPath,
        XDocument manifest,
        IReadOnlyList<VfsFileEntry> files,
        byte[]? registryHive)
    {
        try
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (dir != null)
                Directory.CreateDirectory(dir);

            var outputStream = CreateFileStream(outputPath);

            var factory = (IAppxFactory)new AppxFactory();

            var hashMethodUri = CreateSha256Uri();
            var settings = new APPX_PACKAGE_SETTINGS
            {
                ForceZip32 = false,
                HashMethod = hashMethodUri,
            };

            var writer = factory.CreatePackageWriter(outputStream, ref settings);

            using var packageWriter = new AppxPackageWriter(writer, outputStream);

            foreach (var file in files)
            {
                var fileStream = CreateStreamFromFile(file.SourcePath);
                try
                {
                    var contentType = ContentTypeMapper.GetContentType(file.PackageRelativePath);
                    writer.AddPayloadFile(
                        file.PackageRelativePath,
                        contentType,
                        APPX_COMPRESSION_OPTION.Normal,
                        fileStream);
                }
                finally
                {
                    Marshal.ReleaseComObject(fileStream);
                }
            }

            if (registryHive != null)
            {
                var hiveStream = CreateStreamFromBytes(registryHive);
                try
                {
                    writer.AddPayloadFile(
                        "Registry.dat",
                        "application/octet-stream",
                        APPX_COMPRESSION_OPTION.None,
                        hiveStream);
                }
                finally
                {
                    Marshal.ReleaseComObject(hiveStream);
                }
            }

            var manifestStream = CreateStreamFromXml(manifest);
            try
            {
                writer.Close(manifestStream);
            }
            finally
            {
                Marshal.ReleaseComObject(manifestStream);
            }

            return Result<string>.Success(outputPath);
        }
        catch (COMException ex)
        {
            return Result<string>.Failure(ErrorKind.CompilationError, $"MSIX packaging failed: {ex.Message}");
        }
    }

    private static IStream CreateFileStream(string path)
    {
        var hr = NativeMethods.SHCreateStreamOnFileEx(
            path,
            NativeMethods.STGM_CREATE | NativeMethods.STGM_WRITE | NativeMethods.STGM_SHARE_EXCLUSIVE,
            0x80, // FILE_ATTRIBUTE_NORMAL
            true,
            null,
            out var stream);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        return stream;
    }

    private static IStream CreateStreamFromFile(string path)
    {
        var hr = NativeMethods.SHCreateStreamOnFileEx(
            path,
            NativeMethods.STGM_READ | NativeMethods.STGM_SHARE_DENY_WRITE,
            0,
            false,
            null,
            out var stream);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        return stream;
    }

    private static IStream CreateStreamFromBytes(byte[] data)
    {
        var hr = NativeMethods.CreateStreamOnHGlobal(IntPtr.Zero, true, out var stream);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        stream.Write(data, data.Length, IntPtr.Zero);
        stream.Seek(0, 0 /* STREAM_SEEK_SET */, IntPtr.Zero);
        return stream;
    }

    private static IStream CreateStreamFromXml(XDocument document)
    {
        var hr = NativeMethods.CreateStreamOnHGlobal(IntPtr.Zero, true, out var stream);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        using var writer = new StreamWriter(new ComStreamWrapper(stream), leaveOpen: true);
        document.Save(writer);
        writer.Flush();
        stream.Seek(0, 0 /* STREAM_SEEK_SET */, IntPtr.Zero);
        return stream;
    }

    private static IntPtr CreateSha256Uri()
    {
        var hr = NativeMethods.CreateUri(
            "http://www.w3.org/2001/04/xmlenc#sha256",
            0, // Uri_CREATE_CANONICALIZE
            0,
            out var uri);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
        return uri;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_writer != null)
        {
            Marshal.ReleaseComObject(_writer);
            _writer = null;
        }

        if (_outputStream != null)
        {
            Marshal.ReleaseComObject(_outputStream);
            _outputStream = null;
        }
    }

    private static class NativeMethods
    {
        public const uint STGM_READ = 0x00000000;
        public const uint STGM_WRITE = 0x00000001;
        public const uint STGM_CREATE = 0x00001000;
        public const uint STGM_SHARE_EXCLUSIVE = 0x00000010;
        public const uint STGM_SHARE_DENY_WRITE = 0x00000020;

        [DllImport("shlwapi.dll", EntryPoint = "SHCreateStreamOnFileEx", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateStreamOnFileEx(
            string pszFile,
            uint grfMode,
            uint dwAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool fCreate,
            IStream? pstmTemplate,
            out IStream ppstm);

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int CreateStreamOnHGlobal(
            IntPtr hGlobal,
            [MarshalAs(UnmanagedType.Bool)] bool fDeleteOnRelease,
            out IStream ppstm);

        [DllImport("urlmon.dll", EntryPoint = "CreateUri", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int CreateUri(
            string pwzUri,
            uint dwFlags,
            nuint dwReserved,
            out IntPtr ppUri);
    }

    /// <summary>
    /// Wraps a COM IStream as a managed Stream for use with StreamWriter.
    /// </summary>
    private sealed class ComStreamWrapper : Stream
    {
        private readonly IStream _stream;

        public ComStreamWrapper(IStream stream) => _stream = stream;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (offset == 0 && count == buffer.Length)
            {
                _stream.Write(buffer, count, IntPtr.Zero);
            }
            else
            {
                var segment = new byte[count];
                Buffer.BlockCopy(buffer, offset, segment, 0, count);
                _stream.Write(segment, count, IntPtr.Zero);
            }
        }

        public override void Flush() => _stream.Commit(0);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
