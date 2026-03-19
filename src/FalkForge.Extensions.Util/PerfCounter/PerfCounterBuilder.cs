namespace FalkForge.Extensions.Util.PerfCounter;

public sealed class PerfCounterBuilder
{
    private readonly string _id;
    private string _categoryName = "";
    private string _counterName = "";
    private PerfCounterType _counterType = PerfCounterType.NumberOfItems32;
    private string? _categoryHelp;
    private string? _counterHelp;

    public PerfCounterBuilder(string id) => _id = id;

    public PerfCounterBuilder CategoryName(string name) { _categoryName = name; return this; }
    public PerfCounterBuilder CounterName(string name) { _counterName = name; return this; }
    public PerfCounterBuilder CounterType(PerfCounterType type) { _counterType = type; return this; }
    public PerfCounterBuilder CategoryHelp(string help) { _categoryHelp = help; return this; }
    public PerfCounterBuilder CounterHelp(string help) { _counterHelp = help; return this; }

    internal PerfCounterModel Build() => new()
    {
        Id = _id,
        CategoryName = _categoryName,
        CounterName = _counterName,
        CounterType = _counterType,
        CategoryHelp = _categoryHelp,
        CounterHelp = _counterHelp
    };
}
