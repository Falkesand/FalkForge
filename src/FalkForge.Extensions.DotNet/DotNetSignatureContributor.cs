using FalkForge.Extensibility;

namespace FalkForge.Extensions.DotNet;

/// <summary>
///     Contributes <c>Signature</c> rows describing the sentinel file each planned search's
///     <see cref="DotNetDrLocatorContributor"/> locator resolves against. <c>Signature</c> is not part of
///     this compiler's built-in producer pipeline, so the rows are emitted as a custom table matching the
///     Windows Installer SDK's fixed schema exactly — only <c>FileName</c> and <c>MinVersion</c> are set;
///     <c>MaxVersion</c>/size/date bounds and <c>Languages</c> are left null (no upper bound, no
///     size/date/language constraint on the sentinel file).
/// </summary>
internal sealed class DotNetSignatureContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DotNetSearchPlan> _plans;

    internal DotNetSignatureContributor(IReadOnlyList<DotNetSearchPlan> plans)
    {
        _plans = plans;
    }

    public string TableName => "Signature";

    public IReadOnlyList<ContributedColumn>? WriteColumns { get; } =
    [
        ContributedColumn.Key("Signature"),
        ContributedColumn.Text("FileName", 255, nullable: false),
        ContributedColumn.Text("MinVersion", 20),
        ContributedColumn.Text("MaxVersion", 20),
        new ContributedColumn { Name = "MinSize", Type = ContributedColumnType.Int32, Nullable = true },
        new ContributedColumn { Name = "MaxSize", Type = ContributedColumnType.Int32, Nullable = true },
        new ContributedColumn { Name = "MinDate", Type = ContributedColumnType.Int32, Nullable = true },
        new ContributedColumn { Name = "MaxDate", Type = ContributedColumnType.Int32, Nullable = true },
        ContributedColumn.Text("Languages", 255),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        if (_plans.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(_plans.Count);
        foreach (var plan in _plans)
        {
            rows.Add(new MsiTableRow()
                .Set("Signature", plan.SignatureName)
                .Set("FileName", plan.FileName)
                .Set("MinVersion", plan.MinVersion));
        }

        return rows;
    }
}
