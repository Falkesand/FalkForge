// This assembly's default test-collection parallelism is unsafe for two independent reasons, both
// process-global mutable state that xUnit's per-class-collection concurrency does not isolate:
//
//   1. EngineTrustAnchor (FalkForge.Engine.Integrity) is a freeze-once-per-process singleton by
//      design (a security control: trusted keys must not be registrable once verification has
//      begun). HybridBundleFluentEndToEndTests resets/registers/verifies against it within a single
//      test method, but with parallel collections enabled, *any* other test running concurrently that
//      reaches the real trust-verification path (anything reading EngineTrustAnchor.EffectiveFingerprints)
//      can freeze the singleton mid-sequence and make TrustHybridKey throw "already frozen" —
//      nondeterministically, depending on unrelated scheduling (e.g. which other tests are gated in/out
//      by local NuGet-feed or NativeAOT-publish prerequisites). This is exactly the reason
//      FalkForge.Engine.Tests already disables parallelization (see
//      Logging/EngineMeterCollection.cs) for its own instance of the identical singleton-freeze hazard.
//   2. SelfExtractionModeTests (and others) temporarily redirect the process-wide Console.Out/Console.Error
//      via Console.SetOut/SetError to capture CLI output; those setters are unsynchronized statics, so a
//      concurrently-running test's redirect/restore can interleave with another test's capture window.
//
// Disabling assembly-level parallelization makes every test run one at a time, so registration/reset
// sequences against process-global singletons always complete before any other test can observe or
// mutate that state, and Console.SetOut/SetError redirect windows never overlap. This assembly's default
// test count (~100; heavyweight end-to-end and real-system suites are opt-in via FALKFORGE_E2E /
// FALKFORGE_REAL_SYSTEM_E2E) keeps the wall-clock cost of serial execution small.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
