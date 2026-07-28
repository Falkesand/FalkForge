using System.Reflection;
using System.Runtime.Versioning;
using FalkForge.Platform.Windows;
using Xunit;

namespace FalkForge.Platform.Windows.Tests;

/// <summary>
/// Exercises the REAL <see cref="WindowsMsiApi"/> for its two non-destructive members.
/// <see cref="MsiApiContractTests"/> only reflects over the type (<c>IMsiApi.IsAssignableFrom</c>)
/// and never calls a method; <c>InstallProduct</c>/<c>ConfigureProduct</c> genuinely cannot be
/// safely unit-tested (real, elevated, destructive installs) and are intentionally left alone.
/// <c>SetInternalUI</c>/<c>SetExternalUI</c> are safe, in-process, non-destructive calls, and
/// <c>SetExternalUI</c> is the one member with actual logic: it wraps the caller's
/// <see cref="MsiExternalUIHandler"/> in a managed lambda before handing it to
/// <c>MsiSetExternalUIW</c> — the same "unrooted delegate passed to native code" defect shape
/// this repo has hit before (an FCI callback delegate collected mid-call).
///
/// Empirical finding (verified against the real msi.dll on this machine before writing these
/// assertions): registering an external UI handler, or making a second unrelated msi.dll call
/// afterward (including one that returns ERROR_BAD_QUERY_SYNTAX), does NOT synchronously invoke
/// the handler. Windows Installer only calls the external UI handler from inside an active
/// install/action-sequence engine run (MsiInstallProduct, MsiConfigureProduct, MsiDoAction) —
/// exactly the destructive territory this task says to leave alone. So these tests prove the real
/// call succeeds and that the wrapped native-callback delegate is genuinely rooted for as long as
/// msi.dll can call it (white-box: read back the private static root via reflection, wrap it in a
/// <see cref="WeakReference"/> with no other strong local ever holding the same instance, and
/// confirm it survives several forced GC cycles), rather than proving actual invocation, which is
/// infeasible here without performing a real install.
///
/// Both tests set/restore process-global Windows Installer state (<c>MsiSetInternalUI</c> level,
/// <c>MsiSetExternalUIW</c> registration) and run in an assembly with test parallelization disabled
/// (see the assembly-level <c>CollectionBehavior</c> attribute) so a failed assertion can never
/// leave process-global state that breaks a later test in the same run.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMsiApiRealCallTests
{
    [Fact]
    public void SetInternalUI_RealCall_RoundTripsThroughRealPassthrough()
    {
        IMsiApi api = new WindowsMsiApi();

        // A bare ">= -1" sentinel check is a tautology here: every INSTALLUILEVEL value msi.dll
        // can return already satisfies it, so it can't distinguish the real passthrough from a
        // broken one (e.g. an argument swap or a hardcoded constant). Round-trip instead -- but
        // the probe level must NOT be the same value asserted on the readback: setting 2 first and
        // then asserting the next call's "previous" return equals 2 would also pass against a
        // production stub that always returns the constant 2. INSTALLUILEVEL_BASIC = 3 is the
        // probe (deliberately different from the 2 used below), so only a genuine passthrough --
        // one that reports whatever was actually in effect a moment ago -- can satisfy the
        // assertion.
        var original = api.SetInternalUI(3, nint.Zero);
        try
        {
            // INSTALLUILEVEL_NONE = 2. A non-destructive, process-local UI level change.
            var previousAfterSettingNone = api.SetInternalUI(2, nint.Zero);
            Assert.Equal(3, previousAfterSettingNone);
        }
        finally
        {
            // Restore whatever level was in effect before this test ran -- MsiSetInternalUI is
            // process-global state, and this assembly disables test parallelization specifically
            // so a leaked level here would deterministically affect a later test in the same run.
            api.SetInternalUI(original, nint.Zero);
        }
    }

    [Fact]
    public void SetExternalUI_RealCall_RootsWrappedDelegateAcrossForcedGcThenClearsOnUnregister()
    {
        IMsiApi api = new WindowsMsiApi();
        var callCount = 0;

        MsiExternalUIHandler handler = (_, _, _) =>
        {
            callCount++;
            return 0;
        };

        // WindowsMsiApi.SetExternalUI creates its native-facing wrapper lambda and hands it to
        // MsiSetExternalUIW, which registers it as a process-global callback with no bound call
        // scope and no "you can free this now" notification. [LibraryImport] only roots the
        // marshalled delegate for the duration of the P/Invoke call itself, so the production fix
        // must keep the wrapper reachable from a static field for as long as msi.dll can call it.
        // Read that field back via reflection (white-box, but honest about what it actually proves)
        // rather than asserting on msi.dll's return value: msi.dll returns the raw pointer it was
        // handed regardless of whether the underlying thunk is still alive, so a return-value-only
        // assertion passes identically whether the delegate is rooted or already collected -- it
        // cannot detect this bug.
        var rootField = typeof(WindowsMsiApi).GetField("_rootedHandler", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(rootField); // fails loudly if the static root is ever removed

        try
        {
            var previous = api.SetExternalUI(handler, messageFilter: 0xFFFFFFFF, context: nint.Zero);
            Assert.Equal(nint.Zero, previous); // nothing was registered before this test ran

            // Deliberately no strong local holding the wrapper: a strong local would keep the
            // delegate alive across the forced-GC loop below regardless of whether production
            // roots it, which would make the loop and the "still alive" assertion pass identically
            // whether or not _rootedHandler actually does its job -- proving nothing. Wrapping the
            // reflection read directly in a WeakReference, with no other local ever holding the
            // same instance, means the object's only possible root for the duration of the loop is
            // whatever production itself provides (the static field).
            var weakWrapper = new WeakReference(rootField.GetValue(null));
            Assert.True(weakWrapper.IsAlive); // the wrapper must be reachable from static state

#pragma warning disable S1215 // deliberate forced GC — proving the rooted wrapper survives collection is the point of this test
            for (var i = 0; i < 5; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
            }
#pragma warning restore S1215

            // The wrapper must still be alive after forced GC -- this is what "rooted" means, and
            // with no strong local in play, the only thing that can keep it alive is production's
            // own static field. If that root were ever dropped (e.g. reverted to a plain local
            // variable inside SetExternalUI), the weak reference would report collected here.
            Assert.True(weakWrapper.IsAlive);
            Assert.NotNull(rootField.GetValue(null));

            // A second, unrelated real msi.dll call. If the wrapped delegate/native thunk had been
            // collected and freed, this process would be in undefined territory (best case: still
            // fine, since MsiSetInternalUI never touches the external UI pointer; worst case on a
            // genuinely broken build: a crash). Reaching this line at all is part of the proof.
            var uiLevelResult = api.SetInternalUI(2, nint.Zero);
            Assert.True(uiLevelResult >= -1);

            // Unregister and inspect what msi.dll reports as the "previous" handler pointer. This
            // is non-zero simply because msi.dll hands back whatever raw pointer it had on file --
            // it says nothing about whether that pointer was still backed by a live thunk (that is
            // exactly why the static-field assertions above exist instead of relying on this).
            var previousOnUnregister = api.SetExternalUI(null, messageFilter: 0, context: nint.Zero);
            Assert.NotEqual(nint.Zero, previousOnUnregister);

            // The static root must be cleared once msi.dll has confirmed the unregister swap --
            // holding onto it after that point would leak the wrapper for the rest of the process.
            Assert.Null(rootField.GetValue(null));

            // The handler itself was never actually invoked (see class doc) — assert that
            // explicitly so this test fails loudly instead of silently if msi.dll's behavior here
            // ever changes (e.g. a future Windows Installer version invoking INITIALIZE eagerly).
            Assert.Equal(0, callCount);
        }
        finally
        {
            // Best-effort: if an assertion above failed after registering the handler but before
            // unregistering it, make sure this process-global registration doesn't survive to
            // affect a later test in the same run.
            if (rootField.GetValue(null) is not null)
                api.SetExternalUI(null, messageFilter: 0, context: nint.Zero);
        }
    }
}
