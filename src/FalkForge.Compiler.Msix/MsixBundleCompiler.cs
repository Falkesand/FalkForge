using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msix.Interop;

namespace FalkForge.Compiler.Msix;

[SupportedOSPlatform("windows")]
public sealed class MsixBundleCompiler
{
    public Result<string> Compile(MsixBundleModel model, string outputPath)
    {
        var validationResult = Validate(model);
        if (validationResult.IsFailure)
            return Result<string>.Failure(validationResult.Error);

        return CompileCore(model, outputPath);
    }

    private static Result<Unit> Validate(MsixBundleModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return Result<Unit>.Failure(ErrorKind.Validation, "Bundle name is required.");

        if (model.Packages.Count == 0)
            return Result<Unit>.Failure(ErrorKind.Validation, "At least one package is required.");

        foreach (var pkg in model.Packages)
        {
            if (!File.Exists(pkg.FilePath))
                return Result<Unit>.Failure(ErrorKind.FileNotFound, $"Package file not found: {pkg.FilePath}");
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static Result<string> CompileCore(MsixBundleModel model, string outputPath)
    {
        try
        {
            Directory.CreateDirectory(outputPath);
            var bundleFileName = $"{model.Name}-{model.Version}.msixbundle";
            var bundlePath = Path.Combine(outputPath, bundleFileName);

            var outputStream = CreateFileStream(bundlePath);

            try
            {
                var factory = (IAppxBundleFactory)new AppxBundleFactory();
                var bundleVersion = VersionToUInt64(model.Version);
                var writer = factory.CreateBundleWriter(outputStream, bundleVersion);

                foreach (var pkg in model.Packages)
                {
                    var packageStream = OpenReadStream(pkg.FilePath);
                    try
                    {
                        var fileName = Path.GetFileName(pkg.FilePath);
                        writer.AddPayloadPackage(fileName, packageStream);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(packageStream);
                    }
                }

                writer.Close();
            }
            finally
            {
                Marshal.ReleaseComObject(outputStream);
            }

            if (model.Signing is not null)
            {
                var signResult = SignBundle(bundlePath, model.Signing);
                if (signResult.IsFailure)
                    return Result<string>.Failure(signResult.Error);
            }

            return Result<string>.Success(bundlePath);
        }
        catch (COMException ex)
        {
            return Result<string>.Failure(ErrorKind.CompilationError, $"MSIX bundle creation failed: {ex.Message}");
        }
    }

    private static ulong VersionToUInt64(Version version)
    {
        return ((ulong)(ushort)version.Major << 48) |
               ((ulong)(ushort)version.Minor << 32) |
               ((ulong)(ushort)version.Build << 16) |
               (ulong)(ushort)Math.Max(version.Revision, 0);
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

    private static IStream OpenReadStream(string path)
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

    private static Result<Unit> SignBundle(string bundlePath, Models.SigningOptions signing)
    {
        try
        {
            var args = new List<string> { "sign", "/fd", signing.DigestAlgorithm };

            if (signing.CertificatePath is not null)
                args.AddRange(["/f", signing.CertificatePath]);
            else if (signing.CertificateThumbprint is not null)
                args.AddRange(["/sha1", signing.CertificateThumbprint, "/s", signing.StoreName]);

            if (signing.TimestampUrl is not null)
                args.AddRange(["/tr", signing.TimestampUrl, "/td", signing.DigestAlgorithm]);

            if (signing.Description is not null)
                args.AddRange(["/d", signing.Description]);

            args.Add(bundlePath);

#pragma warning disable S4036 // PATH lookup is the documented contract: signtool.exe ships with the Windows SDK at a version-dependent location
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "signtool.exe",
                Arguments = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
#pragma warning restore S4036

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
                return Result<Unit>.Failure(ErrorKind.CompilationError, "Failed to start signtool.exe");

            process.WaitForExit(TimeSpan.FromMinutes(2));

            if (process.ExitCode != 0)
            {
                var stderr = process.StandardError.ReadToEnd();
                return Result<Unit>.Failure(ErrorKind.CompilationError, $"Signing failed (exit code {process.ExitCode}): {stderr}");
            }

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<Unit>.Failure(ErrorKind.CompilationError, $"Signing failed: {ex.Message}");
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
    }
}
