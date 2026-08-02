namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Pins the fixed <c>RebootPending</c> built-in variable. The original probe checked
/// <c>KeyExists(..., @"SYSTEM\CurrentControlSet\Control\Session Manager\PendingFileRenameOperations")</c>
/// — but <c>PendingFileRenameOperations</c> is a registry VALUE under the <c>Session Manager</c> key,
/// not a key of its own, so a bare <c>OpenSubKey</c> on that path always returns null and the probe
/// was dead code: a package gated on <c>NOT RebootPending</c> would install during exactly the
/// pending-reboot state the author was guarding against. The fix probes the value's existence via
/// <c>IRegistry.TryValueExists</c>, adds the Windows Update <c>RebootRequired</c> key (previously not
/// probed at all), and treats an unreadable probe as pending — an unknown state is not evidence of
/// safety (mirrors the fail-closed precedent on <c>IRegistry.TryReadSubKeyNames</c>).
/// </summary>
public sealed class BuiltInVariablesRebootPendingTests
{
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";
    private const string PendingFileRenameOperationsValue = "PendingFileRenameOperations";
    private const string CbsRebootPendingKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string WindowsUpdateRebootRequiredKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

    [Fact]
    public void Populate_PendingFileRenameOperationsValuePresent_RebootPendingIsOne()
    {
        // Regression test: PendingFileRenameOperations is a VALUE under Session Manager, not a
        // subkey. Setting it via SetStringValue(subKey: SessionManagerKey, valueName: "...") stores
        // it under the mock's "LocalMachine\...\Session Manager" key entry — a different string
        // from the old code's KeyExists(subKey: "...\Session Manager\PendingFileRenameOperations"),
        // so the old implementation genuinely cannot see it (confirmed RED before the fix: the old
        // KeyExists-only probe returned false here because no mock key exists at the
        // "...\Session Manager\PendingFileRenameOperations" path).
        var registry = new MockRegistry()
            .SetStringValue(RegistryRoot.LocalMachine, SessionManagerKey, PendingFileRenameOperationsValue, "\\??\\C:\\old.dll\0\\??\\C:\\new.dll\0\0");
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var rebootPending = store.TryGet<long>(BuiltInVariableNames.RebootPending);
        Assert.True(rebootPending.IsSuccess);
        Assert.Equal(1L, rebootPending.Value);
    }

    [Fact]
    public void Populate_CbsRebootPendingKeyPresent_RebootPendingIsOne()
    {
        var registry = new MockRegistry().AddKey(RegistryRoot.LocalMachine, CbsRebootPendingKey);
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var rebootPending = store.TryGet<long>(BuiltInVariableNames.RebootPending);
        Assert.True(rebootPending.IsSuccess);
        Assert.Equal(1L, rebootPending.Value);
    }

    [Fact]
    public void Populate_WindowsUpdateRebootRequiredKeyPresent_RebootPendingIsOne()
    {
        var registry = new MockRegistry().AddKey(RegistryRoot.LocalMachine, WindowsUpdateRebootRequiredKey);
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var rebootPending = store.TryGet<long>(BuiltInVariableNames.RebootPending);
        Assert.True(rebootPending.IsSuccess);
        Assert.Equal(1L, rebootPending.Value);
    }

    [Fact]
    public void Populate_NoRebootSignalsPresent_RebootPendingIsZero()
    {
        var registry = new MockRegistry();
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var rebootPending = store.TryGet<long>(BuiltInVariableNames.RebootPending);
        Assert.True(rebootPending.IsSuccess);
        Assert.Equal(0L, rebootPending.Value);
    }

    [Fact]
    public void Populate_SessionManagerProbeUnreadable_RebootPendingIsOne()
    {
        // An unreadable probe is not evidence of safety: it must fail closed to "pending", not
        // silently read as "absent".
        var registry = new MockRegistry().FailReadsUnder(SessionManagerKey);
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var rebootPending = store.TryGet<long>(BuiltInVariableNames.RebootPending);
        Assert.True(rebootPending.IsSuccess);
        Assert.Equal(1L, rebootPending.Value);
    }

    [Fact]
    public void Populate_CbsRebootPendingProbeUnreadable_RebootPendingIsOne()
    {
        // Same fail-closed guarantee as the Session Manager probe, but for the CBS RebootPending
        // key: an ACL-denied/unreadable key is not evidence the machine is clean, so it must count
        // as pending rather than silently falling through the plain KeyExists=false path.
        var registry = new MockRegistry().FailReadsUnder(CbsRebootPendingKey);
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var rebootPending = store.TryGet<long>(BuiltInVariableNames.RebootPending);
        Assert.True(rebootPending.IsSuccess);
        Assert.Equal(1L, rebootPending.Value);
    }

    [Fact]
    public void Populate_WindowsUpdateRebootRequiredProbeUnreadable_RebootPendingIsOne()
    {
        // Same fail-closed guarantee as the Session Manager probe, but for the Windows Update
        // RebootRequired key: an ACL-denied/unreadable key must count as pending, not "absent".
        var registry = new MockRegistry().FailReadsUnder(WindowsUpdateRebootRequiredKey);
        var platform = new FakePlatformServices(registry);
        var store = new VariableStore();

        BuiltInVariables.Populate(store, platform);

        var rebootPending = store.TryGet<long>(BuiltInVariableNames.RebootPending);
        Assert.True(rebootPending.IsSuccess);
        Assert.Equal(1L, rebootPending.Value);
    }
}
