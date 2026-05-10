// EngineMeter is a process-global singleton. MeterListener captures used in
// metrics tests can observe emissions from any concurrently-running test that
// exercises pipeline steps or PayloadDownloader. We disable assembly-level
// xUnit parallelization to keep MeterListener captures deterministic; the
// engine test suite is small enough that serial execution does not regress
// wall-clock time meaningfully (~30 s on dev hardware).

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace FalkForge.Engine.Tests.Logging;

using Xunit;

/// <summary>
/// Reserved collection name kept for future per-meter scoping if individual
/// tests ever need ordered execution beyond the assembly default.
/// </summary>
[CollectionDefinition(Name)]
public static class EngineMeterCollection
{
    public const string Name = "EngineMeter";
}
