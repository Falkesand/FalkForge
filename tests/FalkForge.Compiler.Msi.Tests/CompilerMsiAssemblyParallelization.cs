// This assembly's default test-collection parallelism is unsafe: several classes mutate
// process-global environment variables that the compiler under test reads, and xUnit's
// per-class-collection concurrency does not isolate process state. A [Collection] attribute
// serialises the classes *inside* that collection against each other and nothing else, so any
// other collection scheduled concurrently still observes the mutated variable.
//
// The concrete windows, all of which change what a compile PRODUCES rather than merely how it
// reports:
//
//   1. FALKFORGE_NO_SIGN is set to "1" for the duration of a host-MSI compile by
//      IntegrityAttestationSbomToctouTests, IntegritySignaturePayloadHashToctouTests and
//      MsiIntegritySigningTests. It is the only opt-out from integrity signing
//      (MsiAuthoring.PostProcess step 8.5), so a concurrently-compiling test that configured
//      Integrity() would silently receive an MSI with no signature and no sidecar — a failure that
//      looks like a signing bug, not a scheduling artefact. Those three classes share the
//      "SigilProcess" collection, which is exactly why the hazard is not visible from any one of
//      them: the collection protects them from each other, not the rest of the assembly.
//   2. FALKFORGE_GENERATE_SBOM is mutated by LoggingInstrumentationTests and SbomIntegrationTests,
//      which share no collection at all. It decides whether a compile emits an SBOM sidecar, so
//      each can flip the other's expected artefact set.
//   3. MsiIntegritySigningTests sets PATH to string.Empty to prove SigilDetector reports "not
//      available". Anything that resolves an executable during that window sees an empty PATH.
//      The same class also mutates the process-wide, cached SigilDetector result.
//
// Serialising the assembly makes each set/compile/restore sequence complete before any other test
// can observe it. The alternative considered was rebuilding the host MSIs from packages without
// Integrity() so FALKFORGE_NO_SIGN would be unnecessary; rejected because it addresses only window
// 1, leaves 2 and 3 open, and would make those tests compile a package shaped differently from the
// one they then hand to IntegritySigner.
//
// Measured A/B on this exact test population (dev hardware, warm build, two `dotnet test` runs each
// against the built csproj, 1584 passed / 0 failed / 11 skipped both ways):
//
//   parallel   (this line absent) — 2.326s / 2.392s reported test duration; 7.68s / 7.90s wall-clock
//   serialized (this line active) — 11.022s / 11.040s reported test duration; 16.69s / 16.46s wall-clock
//
// So about +8.7s of test time (roughly 4.7x) and +8.8s of wall-clock (roughly 2.1x) for this project.
// That is not free and should not be described as such; it is ~3% on the full-solution build+test
// pipeline this project is normally run inside. The trade was accepted because the cost is bounded
// and constant while the races above are nondeterministic and produce failures that read as compiler
// bugs. Revisit if this project's runtime grows materially — but re-solve all three windows, not just
// the cheapest one.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
