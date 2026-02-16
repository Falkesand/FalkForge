using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Interop;

[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    // MSI Database operations
    [LibraryImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiOpenDatabase(string szDatabasePath, nint szPersist, out nint phDatabase);

    [LibraryImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiDatabaseOpenView(nint hDatabase, string szQuery, out nint phView);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiViewExecute(nint hView, nint hRecord);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiViewModify(nint hView, MsiModify eModifyMode, nint hRecord);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiDatabaseCommit(nint hDatabase);

    [LibraryImport("msi.dll")]
    internal static partial nint MsiCreateRecord(uint cParams);

    [LibraryImport("msi.dll", EntryPoint = "MsiRecordSetStringW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiRecordSetString(nint hRecord, uint iField, string? szValue);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiRecordSetInteger(nint hRecord, uint iField, int iValue);

    [LibraryImport("msi.dll", EntryPoint = "MsiRecordSetStreamW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiRecordSetStream(nint hRecord, uint iField, string szFilePath);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiCloseHandle(nint hAny);

    [LibraryImport("msi.dll")]
    internal static partial nint MsiGetLastErrorRecord();

    [LibraryImport("msi.dll", EntryPoint = "MsiSummaryInfoSetPropertyW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiSummaryInfoSetProperty(nint hSummaryInfo, uint uiProperty, uint uiDataType, int iValue, ref long ftValue, string? szValue);

    [LibraryImport("msi.dll", EntryPoint = "MsiGetSummaryInformationW")]
    internal static partial uint MsiGetSummaryInformation(nint hDatabase, nint szDatabasePath, uint uiUpdateCount, out nint phSummaryInfo);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiSummaryInfoPersist(nint hSummaryInfo);

    [LibraryImport("msi.dll", EntryPoint = "MsiDatabaseImportW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiDatabaseImport(nint hDatabase, string szFolderPath, string szFileName);

    // MSI Modify modes
    internal enum MsiModify : uint
    {
        Refresh = 0,
        Insert = 1,
        Update = 2,
        Assign = 3,
        Replace = 4,
        Delete = 6,
        InsertTemporary = 7,
        Validate = 8,
        ValidateNew = 9,
        ValidateField = 10,
        ValidateDelete = 11,
    }

    // MSI Open database modes
    internal const nint MSIDBOPEN_READONLY = 0;
    internal const nint MSIDBOPEN_TRANSACT = 1;
    internal const nint MSIDBOPEN_DIRECT = 2;
    internal const nint MSIDBOPEN_CREATE = 3;
    internal const nint MSIDBOPEN_CREATEDIRECT = 4;

    // Summary Information Property IDs
    internal const uint PID_CODEPAGE = 1;
    internal const uint PID_TITLE = 2;
    internal const uint PID_SUBJECT = 3;
    internal const uint PID_AUTHOR = 4;
    internal const uint PID_KEYWORDS = 5;
    internal const uint PID_COMMENTS = 6;
    internal const uint PID_TEMPLATE = 7;
    internal const uint PID_LASTAUTHOR = 8;
    internal const uint PID_REVNUMBER = 9;
    internal const uint PID_LASTPRINTED = 11;
    internal const uint PID_CREATE_DTM = 12;
    internal const uint PID_LASTSAVE_DTM = 13;
    internal const uint PID_PAGECOUNT = 14;
    internal const uint PID_WORDCOUNT = 15;
    internal const uint PID_CHARCOUNT = 16;
    internal const uint PID_APPNAME = 18;
    internal const uint PID_SECURITY = 19;

    // VT types for summary info
    internal const uint VT_EMPTY = 0;
    internal const uint VT_I2 = 2;
    internal const uint VT_I4 = 3;
    internal const uint VT_LPSTR = 30;
    internal const uint VT_FILETIME = 64;

    // Database merge
    [LibraryImport("msi.dll", EntryPoint = "MsiDatabaseMergeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiDatabaseMerge(nint hDatabase, nint hDatabaseMerge, string? szTableName);

    // Record reading
    [LibraryImport("msi.dll")]
    internal static partial uint MsiViewFetch(nint hView, out nint phRecord);

    [LibraryImport("msi.dll", EntryPoint = "MsiRecordGetStringW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiRecordGetString(nint hRecord, uint iField, [Out] char[] szValueBuf, ref uint pcchValueBuf);

    [LibraryImport("msi.dll")]
    internal static partial int MsiRecordGetInteger(nint hRecord, uint iField);

    [LibraryImport("msi.dll")]
    internal static partial uint MsiRecordGetFieldCount(nint hRecord);

    [LibraryImport("msi.dll", EntryPoint = "MsiViewGetColumnInfoW")]
    internal static partial uint MsiViewGetColumnInfo(nint hView, uint eColInfoType, out nint phRecord);

    // Transform generation
    [LibraryImport("msi.dll", EntryPoint = "MsiDatabaseGenerateTransformW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiDatabaseGenerateTransform(nint hDatabase, nint hDatabaseReference, string? szTransformFile, int iReserved1, int iReserved2);

    [LibraryImport("msi.dll", EntryPoint = "MsiCreateTransformSummaryInfoW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint MsiCreateTransformSummaryInfo(nint hDatabase, nint hDatabaseReference, string szTransformFile, int iErrorConditions, int iValidation);

    internal const uint ERROR_SUCCESS = 0;
    internal const uint ERROR_NO_MORE_ITEMS = 259;
}
