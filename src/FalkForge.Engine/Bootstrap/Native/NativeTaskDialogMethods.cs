using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FalkForge.Engine.Bootstrap.Native;

/// <summary>
/// P/Invoke declarations for the Windows Task Dialog API (comctl32.dll v6) and
/// the SendMessageW helper (user32.dll) used to update dialog state at runtime.
///
/// Uses DllImport (not LibraryImport) because:
///   - TASKDIALOGCONFIG carries delegate fields and blittable-but-nested structs that
///     the source generator cannot marshal automatically in NativeAOT mode.
///   - The callback delegate requires [UnmanagedFunctionPointer] which is incompatible
///     with [UnmanagedCallersOnly] function pointers when the callee is GC-reachable.
/// This matches the DllImport precedent set by NativeRestartManagerMethods.cs.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeTaskDialogMethods
{
    // ── Button IDs ───────────────────────────────────────────────────────────

    /// <summary>Standard Windows IDCANCEL button identifier (WinUser.h).</summary>
    internal const int IDCANCEL = 2;

    // ── TaskDialog flags (TASKDIALOG_FLAGS) ─────────────────────────────────

    /// <summary>Show a progress bar in the dialog footer.</summary>
    internal const uint TDF_SHOW_PROGRESS_BAR = 0x0008;

    /// <summary>Allow clicking hyperlinks in the content area.</summary>
    internal const uint TDF_ENABLE_HYPERLINKS = 0x0001;

    // ── Common button flags (TASKDIALOG_COMMON_BUTTON_FLAGS) ─────────────────

    /// <summary>Show a Cancel button; sends IDCANCEL when clicked.</summary>
    internal const uint TDCBF_CANCEL_BUTTON = 0x0008;

    // ── Task Dialog Notification codes (TASKDIALOG_NOTIFICATIONS) ────────────

    /// <summary>Sent when the dialog has been created and is about to display.</summary>
    internal const uint TDN_CREATED = 0;

    /// <summary>Sent when a button is clicked; wParam = button id.</summary>
    internal const uint TDN_BUTTON_CLICKED = 2;

    /// <summary>Sent when the dialog is about to be destroyed.</summary>
    internal const uint TDN_DESTROYED = 5;

    // ── Task Dialog Message codes (TDM_*) ────────────────────────────────────
    // All are WM_USER (0x0400) + offset, per CommCtrl.h.

    /// <summary>
    /// TDM_SET_PROGRESS_BAR_POS = WM_USER + 102 (0x0466).
    /// wParam = new position (0..100 when range is default 0–100).
    /// </summary>
    internal const uint TDM_SET_PROGRESS_BAR_POS = 0x0400 + 102; // 0x0466

    /// <summary>
    /// TDM_UPDATE_ELEMENT_TEXT = WM_USER + 114 (0x0472).
    /// wParam = TDE_CONTENT (1) to update the content/message text.
    /// lParam = LPCWSTR new text (caller must keep buffer alive across call).
    /// </summary>
    internal const uint TDM_UPDATE_ELEMENT_TEXT = 0x0400 + 114; // 0x0472

    /// <summary>TDE_CONTENT element id — targets the dialog's main content text.</summary>
    internal const int TDE_CONTENT = 1;

    /// <summary>
    /// TDM_CLICK_BUTTON = WM_USER + 102... actually WM_USER + 6 (0x0406).
    /// wParam = button id. Used to programmatically close the dialog.
    /// </summary>
    internal const uint TDM_CLICK_BUTTON = 0x0400 + 6; // 0x0406

    // ── Callback delegate ────────────────────────────────────────────────────

    /// <summary>
    /// Callback invoked by comctl32 for each dialog notification.
    /// Must be stdcall (the Windows ABI for callbacks).
    /// The delegate is pinned via GCHandle for the dialog lifetime to prevent GC relocation.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int TaskDialogCallbackProc(
        nint hwnd,
        uint uNotification,
        nint wParam,
        nint lParam,
        nint lpRefData);

    // ── Structs ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Subset of TASKDIALOGCONFIG (CommCtrl.h) containing the fields used by
    /// <see cref="TaskDialogProgress"/>. All fields must be in declaration order
    /// to match the native layout (Sequential, Pack=4 on all Windows targets).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    internal struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public nint hwndParent;
        public nint hInstance;
        public uint dwFlags;
        public uint dwCommonButtons;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszWindowTitle;

        public nint hMainIcon;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszMainInstruction;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszContent;

        public uint cButtons;
        public nint pButtons;            // TASKDIALOG_BUTTON* — unused (using common buttons only)
        public int nDefaultButton;
        public uint cRadioButtons;
        public nint pRadioButtons;
        public int nDefaultRadioButton;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszVerificationText;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszExpandedInformation;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszExpandedControlText;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszCollapsedControlText;

        public nint hFooterIcon;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pszFooter;

        public TaskDialogCallbackProc? pfCallback;
        public nint lpCallbackData;
        public uint cxWidth;
    }

    // ── P/Invoke declarations ────────────────────────────────────────────────
    // SYSLIB1054: LibraryImport cannot handle TASKDIALOGCONFIG (delegate field + nested structs
    // requiring manual MarshalAs) in NativeAOT mode without significant boilerplate.
    // DllImport is intentional here — see file-level XML comment for rationale.
#pragma warning disable SYSLIB1054

    /// <summary>
    /// Displays a task dialog and blocks until it is dismissed.
    /// Must be called on a thread with ApartmentState.STA.
    /// </summary>
    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, SetLastError = false,
        ExactSpelling = true)]
    internal static extern int TaskDialogIndirect(
        in TASKDIALOGCONFIG pTaskConfig,
        out int pnButton,
        out int pnRadioButton,
        [MarshalAs(UnmanagedType.Bool)] out bool pfVerificationFlagChecked);

    /// <summary>
    /// Posts a window message to the dialog's HWND.
    /// Used from any thread to send TDM_* messages for dynamic dialog updates.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false,
        ExactSpelling = true)]
    internal static extern nint SendMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

#pragma warning restore SYSLIB1054
}
