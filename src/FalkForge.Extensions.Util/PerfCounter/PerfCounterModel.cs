namespace FalkForge.Extensions.Util.PerfCounter;

public sealed class PerfCounterModel
{
    public required string Id { get; init; }
    public required string CategoryName { get; init; }
    public required string CounterName { get; init; }
    public required PerfCounterType CounterType { get; init; }
    public string? CategoryHelp { get; init; }
    public string? CounterHelp { get; init; }
}
