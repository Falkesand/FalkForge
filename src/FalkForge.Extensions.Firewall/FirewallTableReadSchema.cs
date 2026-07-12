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

    // Original (pre-RemotePort/LocalAddress) column shape. Every WixFirewallException table —
    // however old — carries these 11 columns.
    private static readonly string[] CoreColumns =
    [
        "Name", "RemoteAddresses", "Port", "Protocol", "Program",
        "Profile", "Direction", "Action", "Component_", "Description", "Condition"
    ];

    // Current shape adds the two trailing columns. An MSI authored before they existed lacks
    // them, so a SELECT that names them fails with an unknown-column error — the read then falls
    // back to CoreColumns and defaults RemotePort/LocalAddress to null.
    private static readonly string[] FullColumns =
    [
        "Name", "RemoteAddresses", "Port", "Protocol", "Program",
        "Profile", "Direction", "Action", "Component_", "Description", "Condition",
        "RemotePort", "LocalAddress"
    ];

    public string TableName => "WixFirewallException";

    public Result<IReadOnlyList<object>> ReadErased(ITableQuery query)
    {
        var existsResult = query.TableExists(TableName);
        if (existsResult.IsFailure)
            return Result<IReadOnlyList<object>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<IReadOnlyList<object>>.Success([]);

        // Prefer the full 13-column shape. If that SELECT fails (an older MSI lacks the two
        // trailing columns), retry with the guaranteed 11-column core set and default the
        // absent columns — a shape difference must not surface as a DEC003 read error.
        var rowsResult = query.QueryTable(TableName, FullColumns);
        bool hasTrailing = rowsResult.IsSuccess;
        if (!hasTrailing)
            rowsResult = query.QueryTable(TableName, CoreColumns);

        if (rowsResult.IsFailure)
            return Result<IReadOnlyList<object>>.Failure(
                ErrorKind.Validation,
                $"DEC003: Failed to read WixFirewallException table. {rowsResult.Error.Message}");

        int expectedCells = hasTrailing ? 13 : 11;
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
                Condition:       cells[10],
                RemotePort:      hasTrailing ? cells[11] : null,
                LocalAddress:    hasTrailing ? cells[12] : null));
        }

        return Result<IReadOnlyList<object>>.Success(result);
    }
}
