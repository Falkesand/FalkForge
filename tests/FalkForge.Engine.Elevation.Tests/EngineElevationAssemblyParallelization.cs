// This assembly's default test-collection parallelism is unsafe: ElevationSecurityLog
// (FalkForge.Engine.Elevation) is a process-global static singleton — a static StreamWriter
// field (_writer), an _initialized flag, a _correlationId, an injectable _timeProvider, and a
// _tamperDetected flag, none of it collection-scoped. ElevationSecurityLogTests,
// ElevationSecurityLogCorrelationTests and PidRecyclingDetectionTests all reset, inject and read
// that state directly (via reflection, and via ElevatedHost.IsParentAlive's real
// ElevationSecurityLog.SecurityEvent call on the PID-recycling path), and all three already carry
// a shared [Collection("ElevationSecurityLog")] tag to serialize against each other.
//
// That per-class tag is opt-in, not a structural guarantee: it protects its three members from
// each other and nothing else in the assembly. Any test added later that reaches
// ElevationSecurityLog — directly, or indirectly through ElevatedHost/Program — without also
// remembering the collection tag reintroduces the exact race silently, because xUnit still runs
// that new, untagged collection in parallel with "ElevationSecurityLog". This is the identical
// shape FalkForge.Integration.Tests already hit and fixed the same way (see
// IntegrationAssemblyParallelization.cs): a named collection is fragile because it depends on
// every future caller remembering to opt in; assembly-level DisableTestParallelization is
// opt-out and closes the gap structurally instead of by convention.
//
// Measured: the full-solution run failed twice on unrelated verification runs, a different test
// each time, and passed 205/205 both times FalkForge.Engine.Elevation.Tests ran alone — consistent
// with a scheduling-dependent race rather than a real regression. I was not able to reproduce the
// failure myself in three additional full-solution attempts on this branch before applying this
// fix; the race depends on unrelated scheduling under load, so a clean run does not disprove it,
// the same reasoning the Integration.Tests precedent above already documents. Treat this fix as a
// hypothesis confirmed by code inspection (a genuine process-global singleton with opt-in-only
// protection), not by a caught failure.
//
// Measured A/B on this exact test population (dev hardware, this project's csproj run standalone,
// three `dotnet test` runs each way, 205 passed / 0 failed both ways): parallel (this line
// absent) reported test duration 3.244s / 0.707s / 0.702s, wall-clock 8.41s / 5.71s / 6.07s;
// serialized (this line active) reported test duration 4.118s / 1.359s / 1.357s, wall-clock
// 9.30s / 6.38s / 6.32s. Standalone wall-clock for both shapes is dominated by process/host
// startup; serializing adds well under a second of reported test time. This project is small
// enough that the cost is negligible, unlike the Integration.Tests case.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
