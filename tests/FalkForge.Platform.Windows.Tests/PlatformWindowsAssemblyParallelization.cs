// WindowsMsiApiRealCallTests drives the REAL WindowsMsiApi against msi.dll, mutating process-global
// Windows Installer state: MsiSetInternalUI's UI level and MsiSetExternalUIW's external-UI-handler
// registration (and, since the fix for the unrooted-delegate bug, the WindowsMsiApi._rootedHandler
// static field that backs that registration). None of this is isolated per test collection -- it is
// genuinely one slot per process. With xUnit's default assembly parallelism, a concurrently-running
// test collection could observe or clobber that state mid-test (e.g. see a handler registered by an
// unrelated test, or race the rooted-field assertions across forced GC cycles), producing the exact
// kind of nondeterministic, scheduling-dependent failure this repo has already been bitten by twice
// (see FalkForge.Engine.Tests/Logging/EngineMeterCollection.cs and
// FalkForge.Integration.Tests/IntegrationAssemblyParallelization.cs for the same hazard shape against
// different process-global singletons).
//
// Disabling assembly-level parallelization makes every test in this assembly run one at a time, so a
// set/assert/restore sequence against msi.dll's process-global UI state always completes before any
// other test in this assembly can observe or mutate it.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
