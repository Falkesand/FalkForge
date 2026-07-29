namespace FalkForge.Engine.Tests.RestartManager;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FalkForge.Engine.RestartManager;
using Xunit;

/// <summary>
/// Pins the marshalled memory layout of <c>RM_PROCESS_INFO</c> against the definition in
/// the Windows SDK header <c>restartmanager.h</c>.
///
/// WHY this matters: <c>RmGetList</c> writes an array of native RM_PROCESS_INFO structures
/// directly into the managed buffer. If the managed declaration is even one WCHAR too long,
/// every field after that point is read from the wrong offset AND the array stride is wrong,
/// so entries past index 0 are complete garbage. Nothing else in the codebase can catch that:
/// the struct is only ever populated by the OS, so a wrong layout produces plausible-looking
/// but incorrect process names / restartability flags rather than a crash or a build error.
///
/// The authoritative header definition:
/// <code>
/// #define CCH_RM_MAX_APP_NAME     255
/// #define CCH_RM_MAX_SVC_NAME      63
/// #define CCH_RM_SESSION_KEY      (sizeof(GUID)*2)   /* == 32 */
///
/// typedef struct _RM_PROCESS_INFO {
///     RM_UNIQUE_PROCESS Process;
///     WCHAR             strAppName[CCH_RM_MAX_APP_NAME+1];
///     WCHAR             strServiceShortName[CCH_RM_MAX_SVC_NAME+1];
///     RM_APP_TYPE       ApplicationType;
///     ULONG             AppStatus;
///     DWORD             TSSessionId;
///     BOOL              bRestartable;
/// } RM_PROCESS_INFO;
/// </code>
///
/// Expected layout, computed field by field so a future reader can check the arithmetic
/// (x64 and x86 agree here — every member is 4-byte aligned or smaller):
/// <code>
///   offset   size  field
///        0     12  Process            (RM_UNIQUE_PROCESS: DWORD 4 + FILETIME 8)
///       12    512  strAppName         (WCHAR[256]  = 2 * (255 + 1))
///      524    128  strServiceShortName(WCHAR[64]   = 2 * (63 + 1))
///      652      4  ApplicationType    (RM_APP_TYPE enum == int)
///      656      4  AppStatus          (ULONG)
///      660      4  TSSessionId        (DWORD)
///      664      4  bRestartable       (BOOL, 4-byte Win32 BOOL)
///   -------------
///      668         total
/// </code>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeRestartManagerLayoutTests
{
    [Fact]
    public void RmProcessInfo_MarshalledSize_MatchesRestartManagerHeader()
    {
        // 12 + 512 + 128 + 4 + 4 + 4 + 4 == 668. A declaration that sizes strAppName with
        // CCH_RM_SESSION_KEY (or any other wrong constant) shows up here as a size mismatch.
        Assert.Equal(668, Marshal.SizeOf<NativeRestartManagerMethods.RM_PROCESS_INFO>());
    }

    [Fact]
    public void RmProcessInfo_StrServiceShortName_StartsImmediatelyAfterStrAppName()
    {
        // 12 (Process) + 512 (WCHAR[256] app name) == 524.
        Assert.Equal(
            524,
            Marshal.OffsetOf<NativeRestartManagerMethods.RM_PROCESS_INFO>(
                nameof(NativeRestartManagerMethods.RM_PROCESS_INFO.strServiceShortName)).ToInt64());
    }

    [Fact]
    public void RmProcessInfo_BRestartable_IsTheLastFourBytesOfTheStruct()
    {
        // bRestartable is the field the shutdown decision is made on, and it sits last, so it
        // absorbs the full accumulated drift of every earlier field. 668 - 4 == 664.
        Assert.Equal(
            664,
            Marshal.OffsetOf<NativeRestartManagerMethods.RM_PROCESS_INFO>(
                nameof(NativeRestartManagerMethods.RM_PROCESS_INFO.bRestartable)).ToInt64());
    }

    [Fact]
    public void SessionKeyLength_MatchesRestartManagerHeader()
    {
        // restartmanager.h: #define CCH_RM_SESSION_KEY (sizeof(GUID)*2) — a GUID is 16 bytes,
        // so the session key is 32 characters (a hex rendering of the GUID), NOT 256.
        // Pinned so a future edit cannot quietly restore the wrong value and shift every
        // RM_PROCESS_INFO field along with it.
        Assert.Equal(32, NativeRestartManagerMethods.CCH_RM_SESSION_KEY);
    }

    [Fact]
    public void BufferLengthConstants_MatchRestartManagerHeader()
    {
        // restartmanager.h: #define CCH_RM_MAX_APP_NAME 255 / #define CCH_RM_MAX_SVC_NAME 63.
        // Both are lengths EXCLUDING the null terminator, hence the +1 at each declaration site.
        // Pinned separately from the offset assertions so that a wrong constant is reported as
        // exactly that, rather than as an unexplained byte offset drift.
        Assert.Equal(255, NativeRestartManagerMethods.CCH_RM_MAX_APP_NAME);
        Assert.Equal(63, NativeRestartManagerMethods.CCH_RM_MAX_SVC_NAME);
    }
}
