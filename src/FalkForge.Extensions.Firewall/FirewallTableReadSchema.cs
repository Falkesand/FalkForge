using FalkForge.Extensibility;

namespace FalkForge.Extensions.Firewall;

/// <summary>
/// Read-side schema for the MSI <c>WixFirewallException</c> table.
/// Implements <see cref="ITableReadSchema"/> so it can be returned from
/// <see cref="FirewallTableContributor.ReadSchema"/> without referencing the
/// Decompiler assembly — only <see cref="ITableQuery"/> from Extensibility is used.
/// </summary>
internal sealed class FirewallTableReadSchema : ITableReadSchema
{
    internal static readonly FirewallTableReadSchema Instance = new();

    private static readonly string[] Columns =
    [
        "Name", "RemoteAddresses", "Port", "Protocol", "Program",
        "Profile", "Direction", "Action", "Component_", "Description", "Condition"
    ];

    public string TableName => "WixFirewallException";

    public Result<IReadOnlyList<object>> ReadErased(ITableQuery query)
    {
        var existsResult = query.TableExists(TableName);
        if (existsResult.IsFailure)
            return Result<IReadOnlyList<object>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<IReadOnlyList<object>>.Success([]);

        var rowsResult = query.QueryTable(TableName, Columns);
        if (rowsResult.IsFailure)
            return Result<IReadOnlyList<object>>.Failure(
                ErrorKind.Validation,
                $"DEC003: Failed to read WixFirewallException table. {rowsResult.Error.Message}");

        const int expectedCells = 11;
        var result = new List<object>(rowsResult.Value.Count);

        for (var i = 0; i < rowsResult.Value.Count; i++)
        {
            var cells = rowsResult.Value[i];
            if (cells.Length < expectedCells)
                return Result<IReadOnlyList<object>>.Failure(
                    ErrorKind.Validation,
                    $"DEC003: WixFirewallException row {i} has {cells.Length} cells; expected {expectedCells}.");

            if (!int.TryParse(cells[3], out var protocol))
                return Result<IReadOnlyList<object>>.Failure(
                    ErrorKind.Validation,
                    $"DEC003: WixFirewallException row {i} Protocol '{cells[3]}' is not a valid integer.");
            if (!int.TryParse(cells[5], out var profile))
                return Result<IReadOnlyList<object>>.Failure(
                    ErrorKind.Validation,
                    $"DEC003: WixFirewallException row {i} Profile '{cells[5]}' is not a valid integer.");
            if (!int.TryParse(cells[6], out var direction))
                return Result<IReadOnlyList<object>>.Failure(
                    ErrorKind.Validation,
                    $"DEC003: WixFirewallException row {i} Direction '{cells[6]}' is not a valid integer.");
            if (!int.TryParse(cells[7], out var action))
                return Result<IReadOnlyList<object>>.Failure(
                    ErrorKind.Validation,
                    $"DEC003: WixFirewallException row {i} Action '{cells[7]}' is not a valid integer.");

            result.Add(new WixFirewallExceptionRow(
                Name:            cells[0] ?? string.Empty,
                RemoteAddresses: cells[1],
                Port:            cells[2],
                Protocol:        protocol,
                Program:         cells[4],
                Profile:         profile,
                Direction:       direction,
                Action:          action,
                Component_:      cells[8],
                Description:     cells[9],
                Condition:       cells[10]));
        }

        return Result<IReadOnlyList<object>>.Success(result);
    }
}
