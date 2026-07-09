using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace FalkForge.Compiler.Msix.Registry;

[SupportedOSPlatform("windows")]
internal static class RegistryHiveBuilder
{
    public static Result<byte[]> Build(IReadOnlyList<MsixRegistryEntry> entries)
    {
        if (entries.Count == 0)
            return Result<byte[]>.Failure(ErrorKind.InvalidConfiguration, "No registry entries to build hive from.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"msix-reg-{Guid.NewGuid():N}.dat");
        try
        {
            var result = NativeMethods.ORCreateHive(out var hiveHandle);
            if (result != 0)
                return Result<byte[]>.Failure(ErrorKind.CompilationError, $"Failed to create registry hive: error {result}");

            try
            {
                foreach (var entry in entries)
                {
                    var keyPath = MapRootToHivePath(entry.Root, entry.Key);

                    var createResult = CreateKeyPath(hiveHandle, keyPath);
                    if (createResult.IsFailure)
                        return Result<byte[]>.Failure(createResult.Error);
                    var keyHandle = createResult.Value;

                    try
                    {
                        if (entry.ValueName is not null || entry.Value is not null)
                        {
                            var (regType, data) = ConvertValue(entry);
                            result = NativeMethods.ORSetValue(keyHandle, entry.ValueName, (uint)regType, data, (uint)data.Length);
                            if (result != 0)
                                return Result<byte[]>.Failure(ErrorKind.CompilationError, $"Failed to set registry value '{entry.ValueName}': error {result}");
                        }
                    }
                    finally
                    {
                        // CA1806: best-effort cleanup close — nothing actionable if it fails.
                        _ = NativeMethods.ORCloseKey(keyHandle);
                    }
                }

                // MSIX targets Windows 10+; ORSaveHive requires valid OS version (0,0 returns ERROR_INVALID_PARAMETER).
                result = NativeMethods.ORSaveHive(hiveHandle, tempPath, 10u, 0u);
                if (result != 0)
                    return Result<byte[]>.Failure(ErrorKind.CompilationError, $"Failed to save registry hive: error {result}");
            }
            finally
            {
                // CA1806: best-effort cleanup close — nothing actionable if it fails.
                _ = NativeMethods.ORCloseHive(hiveHandle);
            }

            return Result<byte[]>.Success(File.ReadAllBytes(tempPath));
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Creates all intermediate keys in the path, since offreg.dll's ORCreateKey
    /// does not automatically create parent keys.
    /// Returns a handle to the leaf key (caller must close it).
    /// </summary>
    private static Result<nint> CreateKeyPath(nint hiveHandle, string fullPath)
    {
        var segments = fullPath.Split('\\');
        var currentPath = new StringBuilder();
        nint leafHandle = nint.Zero;

        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
                currentPath.Append('\\');
            currentPath.Append(segments[i]);

            var path = currentPath.ToString();
            var result = NativeMethods.ORCreateKey(hiveHandle, path, null, 0, nint.Zero, out var keyHandle, out _);
            if (result != 0)
                return Result<nint>.Failure(ErrorKind.CompilationError, $"Failed to create registry key '{path}': error {result}");

            // Close intermediate keys, keep only the leaf
            // CA1806: best-effort cleanup close — nothing actionable if it fails.
            if (i < segments.Length - 1)
                _ = NativeMethods.ORCloseKey(keyHandle);
            else
                leafHandle = keyHandle;
        }

        return Result<nint>.Success(leafHandle);
    }

    private static string MapRootToHivePath(string root, string key) => root.ToUpperInvariant() switch
    {
        "HKCU" => $"REGISTRY\\USER\\{key}",
        "HKLM" => $"REGISTRY\\MACHINE\\{key}",
        _ => key
    };

    private static (int RegType, byte[] Data) ConvertValue(MsixRegistryEntry entry)
    {
        return entry.Type switch
        {
            MsixRegistryValueType.String => (1, Encoding.Unicode.GetBytes((entry.Value ?? "") + "\0")),
            MsixRegistryValueType.ExpandString => (2, Encoding.Unicode.GetBytes((entry.Value ?? "") + "\0")),
            MsixRegistryValueType.DWord => (4, BitConverter.GetBytes(int.Parse(entry.Value ?? "0", System.Globalization.CultureInfo.InvariantCulture))),
            MsixRegistryValueType.QWord => (11, BitConverter.GetBytes(long.Parse(entry.Value ?? "0", System.Globalization.CultureInfo.InvariantCulture))),
            MsixRegistryValueType.Binary => (3, Convert.FromHexString(entry.Value ?? "")),
            MsixRegistryValueType.MultiString => (7, ConvertMultiString(entry.Value)),
            _ => (1, Encoding.Unicode.GetBytes((entry.Value ?? "") + "\0"))
        };
    }

    private static byte[] ConvertMultiString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return [0, 0, 0, 0]; // double null terminator

        var parts = value.Split(';');
        using var ms = new MemoryStream();
        foreach (var part in parts)
        {
            var bytes = Encoding.Unicode.GetBytes(part + "\0");
            ms.Write(bytes);
        }

        ms.Write([0, 0]); // final null terminator
        return ms.ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        private const string OffregLib = "offreg.dll";

        [DllImport(OffregLib, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int ORCreateHive(out nint hiveHandle);

        [DllImport(OffregLib, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int ORCreateKey(
            nint hiveHandle,
            string subKey,
            string? lpClass,
            uint dwOptions,
            nint pSecurityDescriptor,
            out nint keyHandle,
            out uint disposition);

        [DllImport(OffregLib, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int ORSetValue(
            nint keyHandle,
            string? valueName,
            uint type,
            byte[] data,
            uint dataLength);

        [DllImport(OffregLib, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int ORSaveHive(
            nint hiveHandle,
            string hivePath,
            uint majorVersion,
            uint minorVersion);

        [DllImport(OffregLib, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int ORCloseKey(nint keyHandle);

        [DllImport(OffregLib, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern int ORCloseHive(nint hiveHandle);
    }
}
