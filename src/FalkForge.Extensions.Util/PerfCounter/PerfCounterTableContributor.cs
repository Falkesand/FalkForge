using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.PerfCounter;

public sealed class PerfCounterTableContributor : IMsiTableContributor
{
    private readonly List<PerfCounterModel> _counters = [];

    public string TableName => "FalkForgePerfCounter";

    public void Add(PerfCounterModel counter) => _counters.Add(counter);

    public IReadOnlyList<PerfCounterModel> Counters => _counters;

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_counters.Count);

        foreach (var counter in _counters)
        {
            var row = new MsiTableRow()
                .Set("Id", counter.Id)
                .Set("CategoryName", counter.CategoryName)
                .Set("CounterName", counter.CounterName)
                .Set("CounterType", (int)counter.CounterType)
                .Set("CategoryHelp", counter.CategoryHelp)
                .Set("CounterHelp", counter.CounterHelp);

            rows.Add(row);
        }

        return rows;
    }
}
