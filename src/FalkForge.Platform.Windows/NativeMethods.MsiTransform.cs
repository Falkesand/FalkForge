using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Platform.Windows;

// msi.dll database + transform-generation bindings, ported from FalkForge.Compiler.Msi so the
// NativeAOT elevation companion (which must not reference the compiler assembly) can build a
// property-setting transform itself. LibraryImport keeps this source-generated and AOT-safe. The
// assembly-level DefaultDllImportSearchPaths(System32) attribute is declared in NativeMethods.Msi.cs.
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    // MsiOpenDatabase persist modes.
    internal const nint MsiDbOpenReadOnly = 0;
    internal const nint MsiDbOpenTransact = 1;

    // MsiViewModify modes.
    internal const uint MsiModifyInsert = 1;
    internal const uint MsiModifyUpdate = 2;

    internal const uint ErrorMoreData = 234;
    internal const uint ErrorNoMoreItems = 259;

    [LibraryImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiOpenDatabase(string szDatabasePath, nint szPersist, out nint phDatabase);

    [LibraryImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiDatabaseOpenView(nint hDatabase, string szQuery, out nint phView);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiViewExecute(nint hView, nint hRecord);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiViewFetch(nint hView, out nint phRecord);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiViewModify(nint hView, uint eModifyMode, nint hRecord);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiDatabaseCommit(nint hDatabase);

    [LibraryImport("msi.dll")]
    internal static partial nint MsiCreateRecord(uint cParams);

    [LibraryImport("msi.dll", EntryPoint = "MsiRecordSetStringW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiRecordSetString(nint hRecord, uint iField, string? szValue);

    [LibraryImport("msi.dll", EntryPoint = "MsiRecordGetStringW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiRecordGetString(nint hRecord, uint iField, [Out] char[] szValueBuf,
        ref uint pcchValueBuf);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiCloseHandle(nint hAny);

    [LibraryImport("msi.dll", EntryPoint = "MsiDatabaseGenerateTransformW",
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiDatabaseGenerateTransform(nint hDatabase, nint hDatabaseReference,
        string? szTransformFile, int iReserved1, int iReserved2);

    [LibraryImport("msi.dll", EntryPoint = "MsiCreateTransformSummaryInfoW",
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiCreateTransformSummaryInfo(nint hDatabase, nint hDatabaseReference,
        string szTransformFile, int iErrorConditions, int iValidation);
}
