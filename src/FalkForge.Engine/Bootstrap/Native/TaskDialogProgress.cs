using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static FalkForge.Engine.Bootstrap.Native.NativeTaskDialogMethods;

namespace FalkForge.Engine.Bootstrap.Native;

// ── Seam interface ────────────────────────────────────────────────────────────

/// <summary>
/// Abstraction over the native TaskDialog hosting layer.
/// Production code uses <see cref="NativeDialogDriver"/>; tests inject a fake.
/// </summary>
internal interface IDialogDriver
{
    /// <summary>
    /// Starts the dialog. Implementations may block (native STA thread) or return
    /// immediately (test fake). Called once per dialog lifetime.
    /// </summary>
    void Run(string title, string message);

    /// <summary>
    /// Sends a TDM_* window message to the live dialog.
    /// Safe to call from any thread; implementations must handle cross-thread dispatch.
    /// </summary>
    void Send(uint message, nint wParam, nint lParam);

    /// <summary>
    /// Raised by the driver when a button inside the dialog is clicked.
    /// The argument is the button id (e.g. <see cref="IDCANCEL"/> = 2).
    /// </summary>
    event Action<int>? ButtonClicked;
}

// ── Production driver ─────────────────────────────────────────────────────────

/// <summary>
/// Production <see cref="IDialogDriver"/> that hosts <c>TaskDialogIndirect</c> on a
/// dedicated STA background thread.  Other threads update dialog state via
/// <c>SendMessageW</c> using the HWND captured in the creation callback.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NativeDialogDriver : IDialogDriver, IDisposable
{
    private readonly Thread _staThread;
    private volatile nint _hwnd;           // set by TDN_CREATED callback; read by Send()
    private GCHandle _callbackPin;         // keeps callback delegate alive during dialog
    private int _disposed;

    public event Action<int>? ButtonClicked;

    public NativeDialogDriver()
    {
        _staThread = new Thread(DialogThreadProc) { IsBackground = true, Name = "TaskDialogSTA" };
        _staThread.SetApartmentState(ApartmentState.STA);
    }

    // State passed from Run() to the STA thread proc via captured fields
    private string _title = string.Empty;
    private string _message = string.Empty;

    /// <inheritdoc/>
    public void Run(string title, string message)
    {
        _title = title;
        _message = message;
        _staThread.Start();
    }

    /// <inheritdoc/>
    public void Send(uint message, nint wParam, nint lParam)
    {
        nint hwnd = _hwnd;
        if (hwnd != 0)
            SendMessageW(hwnd, message, wParam, lParam);
    }

    private void DialogThreadProc()
    {
        // Keep the delegate alive for the dialog's lifetime.
        // GCHandleType.Normal (not Pinned) is correct here: comctl32 holds a pointer to the
        // CLR-generated native thunk, not to the managed delegate object itself. The thunk is
        // stable as long as the delegate object is alive. Normal prevents GC collection;
        // Pinned would additionally prevent heap compaction of the object, which is unnecessary
        // and wastes GC resources for reference-type delegates.
        TaskDialogCallbackProc callback = OnCallback;
        _callbackPin = GCHandle.Alloc(callback, GCHandleType.Normal);

        var config = new TASKDIALOGCONFIG
        {
            cbSize = (uint)Marshal.SizeOf<TASKDIALOGCONFIG>(),
            dwFlags = TDF_SHOW_PROGRESS_BAR | TDF_ENABLE_HYPERLINKS,
            dwCommonButtons = TDCBF_CANCEL_BUTTON,
            pszWindowTitle = _title,
            pszContent = _message,
            pfCallback = callback,
        };

        // HRESULT captured but not acted on: dialog failure (e.g. comctl32 v6 unavailable)
        // results in the Cancel event never firing, which the caller handles via timeout.
        // E_FAIL is acceptable here — treated as silent dialog-not-shown.
        int hr = TaskDialogIndirect(in config, out _, out _, out _);
        GC.KeepAlive(hr); // suppress CA1806; failure is intentionally silent

        // Dialog returned — release pin
        if (_callbackPin.IsAllocated)
            _callbackPin.Free();
    }

    private int OnCallback(nint hwnd, uint uNotification, nint wParam, nint lParam, nint lpRefData)
    {
        switch (uNotification)
        {
            case TDN_CREATED:
                _hwnd = hwnd;
                break;

            case TDN_BUTTON_CLICKED:
                ButtonClicked?.Invoke((int)wParam);
                break;

            case TDN_DESTROYED:
                _hwnd = 0;
                break;
        }

        return 0; // S_OK — allow default processing
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Close dialog if still open
        Send(TDM_CLICK_BUTTON, IDCANCEL, 0);

        // Wait for STA thread to exit (dialog closed synchronously on that thread)
        if (_staThread.IsAlive)
            _staThread.Join(timeout: TimeSpan.FromSeconds(5));

        if (_callbackPin.IsAllocated)
            _callbackPin.Free();
    }
}

// ── Public wrapper ────────────────────────────────────────────────────────────

/// <summary>
/// Managed wrapper over a comctl32 v6 task dialog, providing a progress bar and
/// a Cancel button for use during pre-UI prerequisite bootstrap.
///
/// Threading model: <see cref="Show"/> starts a dedicated STA background thread that
/// owns the dialog message pump. All other methods (<see cref="SetMessage"/>,
/// <see cref="SetPercent"/>, <see cref="Close"/>) send window messages from the
/// caller's thread via <c>SendMessageW</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TaskDialogProgress : IDisposable
{
    private readonly IDialogDriver _driver;
    private readonly string _title;
    private readonly string _initialMessage;
    private int _disposed;

    // Current content string pointer — allocated via Marshal.StringToHGlobalUni,
    // held as a field so the buffer stays alive across the SendMessageW call,
    // and freed on the next SetMessage call or Dispose.
    private nint _contentBuffer;

    /// <summary>
    /// Raised when the user clicks Cancel inside the dialog.
    /// May be raised from the dialog's STA thread.
    /// </summary>
    public event EventHandler? Cancel;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes a new instance bound to the production native driver.
    /// </summary>
    public TaskDialogProgress(string title, string initialMessage)
        : this(title, initialMessage, new NativeDialogDriver()) { }

    private TaskDialogProgress(string title, string initialMessage, IDialogDriver driver)
    {
        _title = title;
        _initialMessage = initialMessage;
        _driver = driver;
        _driver.ButtonClicked += OnButtonClicked;
    }

    /// <summary>
    /// Creates a <see cref="TaskDialogProgress"/> backed by a test-supplied driver.
    /// Allows unit tests to exercise cancel wiring and progress clamping without
    /// showing a real dialog.
    /// </summary>
    internal static TaskDialogProgress CreateForTesting(IDialogDriver driver)
        => new(string.Empty, string.Empty, driver);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Shows the progress dialog. Returns immediately; dialog runs on a background STA thread.</summary>
    public void Show() => _driver.Run(_title, _initialMessage);

    /// <summary>
    /// Updates the dialog's content/message text.
    /// Not thread-safe for concurrent callers: the <see cref="_contentBuffer"/> swap is not
    /// atomic. Bootstrap usage is single-threaded on the main thread so this is not a concern
    /// in practice.
    /// </summary>
    public void SetMessage(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        // Free previous buffer; allocate new one.  Buffer must outlive the SendMessageW call.
        FreeContentBuffer();
        _contentBuffer = Marshal.StringToHGlobalUni(message);
        _driver.Send(TDM_UPDATE_ELEMENT_TEXT, TDE_CONTENT, _contentBuffer);
    }

    /// <summary>
    /// Updates the progress bar position. Value is clamped to [0, 100].
    /// Safe to call from any thread.
    /// </summary>
    public void SetPercent(int percent)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        int clamped = Math.Clamp(percent, 0, 100);
        _driver.Send(TDM_SET_PROGRESS_BAR_POS, (nint)clamped, 0);
    }

    /// <summary>Programmatically closes the dialog as if the user clicked Cancel.</summary>
    public void Close()
    {
        if (_disposed != 0) return;
        _driver.Send(TDM_CLICK_BUTTON, IDCANCEL, 0);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _driver.ButtonClicked -= OnButtonClicked;

        if (_driver is IDisposable d)
            d.Dispose();

        FreeContentBuffer();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void OnButtonClicked(int buttonId)
    {
        if (buttonId == IDCANCEL)
            Cancel?.Invoke(this, EventArgs.Empty);
    }

    private void FreeContentBuffer()
    {
        if (_contentBuffer != 0)
        {
            Marshal.FreeHGlobal(_contentBuffer);
            _contentBuffer = 0;
        }
    }
}
