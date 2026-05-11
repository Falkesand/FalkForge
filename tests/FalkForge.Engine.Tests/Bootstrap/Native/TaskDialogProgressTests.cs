namespace FalkForge.Engine.Tests.Bootstrap.Native;

using System.Runtime.Versioning;
using FalkForge.Engine.Bootstrap.Native;
using Xunit;

/// <summary>
/// Tests for <see cref="TaskDialogProgress"/> cancel-callback wiring and pure-logic behaviour.
/// All tests are headless — no real dialog is shown. A fake <see cref="IDialogDriver"/> is
/// injected so the callback path can be exercised without comctl32.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TaskDialogProgressTests
{
    // ── Fake driver ──────────────────────────────────────────────────────────

    /// <summary>
    /// Test double for <see cref="IDialogDriver"/>.
    /// Stores sent messages and lets tests fire button-click notifications directly.
    /// </summary>
    private sealed class FakeDialogDriver : IDialogDriver
    {
        // Messages recorded by Send()
        public List<(uint Msg, nint WParam, nint LParam)> SentMessages { get; } = [];

        // Raise this to simulate the user clicking a button inside the dialog.
        public event Action<int>? ButtonClicked;

        /// <summary>Fires a simulated TDN_BUTTON_CLICKED for the given button id.</summary>
        public void SimulateButtonClick(int buttonId) => ButtonClicked?.Invoke(buttonId);

        // IDialogDriver — Run does nothing (no native call, no STA thread).
        public void Run(string title, string message) { }

        // IDialogDriver — Send records the call.
        public void Send(uint message, nint wParam, nint lParam)
            => SentMessages.Add((message, wParam, lParam));

        // IDialogDriver — expose the event so TaskDialogProgress can subscribe.
        event Action<int>? IDialogDriver.ButtonClicked
        {
            add => ButtonClicked += value;
            remove => ButtonClicked -= value;
        }
    }

    // ── Test 1 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that clicking the Cancel button (IDCANCEL = 2) inside the dialog
    /// raises <see cref="TaskDialogProgress.Cancel"/> exactly once.
    /// This exercises the callback wiring without showing a real dialog.
    /// </summary>
    [Fact]
    public void TaskDialogProgress_RaisesCancel_WhenDriverReportsCancelButton()
    {
        // Arrange
        var driver = new FakeDialogDriver();
        using var dialog = TaskDialogProgress.CreateForTesting(driver);

        int cancelRaisedCount = 0;
        dialog.Cancel += (_, _) => cancelRaisedCount++;

        // Act — simulate user clicking Cancel (IDCANCEL = 2)
        driver.SimulateButtonClick(NativeTaskDialogMethods.IDCANCEL);

        // Assert — event raised exactly once
        Assert.Equal(1, cancelRaisedCount);
    }

    // ── Test 2 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="TaskDialogProgress.SetPercent"/> clamps out-of-range
    /// values: negatives become 0, values above 100 become 100.
    /// This is pure logic; no native call is made.
    /// </summary>
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(150, 100)]
    public void SetPercent_ClampsToZeroToOneHundred(int input, int expectedClamped)
    {
        // Arrange
        var driver = new FakeDialogDriver();
        using var dialog = TaskDialogProgress.CreateForTesting(driver);

        // Act
        dialog.SetPercent(input);

        // Assert — the TDM_SET_PROGRESS_BAR_POS message was sent with the clamped value
        Assert.Single(driver.SentMessages);
        (uint msg, nint wParam, nint lParam) = driver.SentMessages[0];
        Assert.Equal(NativeTaskDialogMethods.TDM_SET_PROGRESS_BAR_POS, msg);
        Assert.Equal((nint)expectedClamped, wParam);
        _ = lParam; // lParam unused for this message
    }

    // ── Test 3 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that calling <see cref="TaskDialogProgress.Dispose"/> twice does not throw.
    /// Idempotent disposal is required because consumer code (using + explicit close) may
    /// dispose multiple times.
    /// </summary>
    [Fact]
    public void Dispose_IsIdempotent()
    {
        // Arrange
        var driver = new FakeDialogDriver();
        var dialog = TaskDialogProgress.CreateForTesting(driver);

        // Act & Assert — neither call must throw
        var ex = Record.Exception(() =>
        {
            dialog.Dispose();
            dialog.Dispose();
        });
        Assert.Null(ex);
    }
}
