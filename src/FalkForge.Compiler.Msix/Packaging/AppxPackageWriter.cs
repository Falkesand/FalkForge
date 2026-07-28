using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Xml.Linq;
using FalkForge.Compiler.Msix.Interop;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace FalkForge.Compiler.Msix.Packaging;

/// <summary>
/// Result of a successful <see cref="AppxPackageWriter.CreatePackage"/> call: the produced
/// package path plus the SHA-256 hash of every payload file's bytes, captured at the moment
/// they were read for embedding. Downstream consumers (the SBOM sidecar) use these hashes
/// instead of reopening the source paths later, so a source-file mutation after packaging
/// cannot desync the sidecar from what actually shipped in the signed package.
/// </summary>
internal readonly record struct MsixPackageResult(string OutputPath, IReadOnlyDictionary<string, string> PayloadHashes);

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

    public static Result<MsixPackageResult> CreatePackage(
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

            IAppxPackageWriter writer;
            try
            {
                var hashMethodUri = CreateSha256Uri();
                try
                {
                    var settings = new APPX_PACKAGE_SETTINGS
                    {
                        ForceZip32 = false,
                        HashMethod = hashMethodUri,
                    };

                    writer = factory.CreatePackageWriter(outputStream, ref settings);
                }
                finally
                {
                    // hashMethodUri is a raw IUri* with no RCW (urlmon CreateUri, not a
                    // .NET-visible COM class). CreatePackageWriter AddRefs it if it retains a
                    // reference, so our own ref must be dropped here regardless of success/failure
                    // or it leaks for process lifetime (never freed by any finalizer).
                    Marshal.Release(hashMethodUri);
                }
            }
            finally
            {
                // The factory's job ends once it has produced the writer; the writer holds its
                // own independent COM references, so releasing our factory RCW here does not
                // affect it. This is an RCW (unlike hashMethodUri), so the CLR finalizer would
                // eventually release it anyway — releasing explicitly just tightens the window.
                Marshal.ReleaseComObject(factory);
            }

            using var packageWriter = new AppxPackageWriter(writer, outputStream);

            var payloadHashes = new Dictionary<string, string>(files.Count);

            foreach (var file in files)
            {
                // Hash the exact bytes about to be embedded, in the same pass that copies them
                // into the package. Hashing later — after packaging AND signing complete, as the
                // SBOM step used to — would let a source-file mutation in that window desync the
                // sidecar from what actually shipped in the signed package (CodeRabbit #3658582425).
                if (File.Exists(file.SourcePath))
                {
                    using var hashStream = File.OpenRead(file.SourcePath);
                    payloadHashes[file.PackageRelativePath] = Convert.ToHexString(SHA256.HashData(hashStream));
                }

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

            return Result<MsixPackageResult>.Success(new MsixPackageResult(outputPath, payloadHashes));
        }
        catch (COMException ex)
        {
            return Result<MsixPackageResult>.Failure(ErrorKind.CompilationError, $"MSIX packaging failed: {ex.Message}");
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
