using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Platform.Windows;

// wintrust.dll bindings for Authenticode trust verification.
// Style matches NativeMethods.Msi.cs: LibraryImport on a [SupportedOSPlatform("windows")]
// partial class; the assembly-level DefaultDllImportSearchPaths(System32) attribute in
// NativeMethods.Msi.cs hardens DLL resolution for every partial in this class.
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    // WINTRUST_ACTION_GENERIC_VERIFY_V2 — {00AAC56B-CD44-11d0-8CC2-00C04FC295EE}.
    // Standard Authenticode policy: file hash matches the embedded signature AND the
    // signing certificate chains to a trusted root.
    internal static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new(0x00AAC56B, 0xCD44, 0x11D0, 0x8C, 0xC2, 0x00, 0xC0, 0x4F, 0xC2, 0x95, 0xEE);

    internal const uint WTD_UI_NONE = 2;       // never show a UI prompt (headless installer)
    internal const uint WTD_REVOKE_NONE = 0;    // do not perform online revocation checks
    internal const uint WTD_CHOICE_FILE = 1;    // verify an embedded file signature
    internal const uint WTD_STATE_ACTION_VERIFY = 1;
    internal const uint WTD_STATE_ACTION_CLOSE = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public nint pcwszFilePath;   // LPCWSTR, set via Marshal.StringToHGlobalUni
        public nint hFile;           // optional file handle; NULL lets WinTrust open it
        public nint pgKnownSubject;  // optional known subject GUID; NULL
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINTRUST_DATA
    {
        public uint cbStruct;
        public nint pPolicyCallbackData;
        public nint pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public nint pFile;           // -> WINTRUST_FILE_INFO when dwUnionChoice == WTD_CHOICE_FILE
        public uint dwStateAction;
        public nint hWVTStateData;
        public nint pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public nint pSignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    internal static partial int WinVerifyTrust(nint hwnd, in Guid pgActionID, nint pWVTData);
}
