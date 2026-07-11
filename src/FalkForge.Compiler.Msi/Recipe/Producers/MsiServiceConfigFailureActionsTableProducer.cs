using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>MsiServiceConfigFailureActions</c> table. Walks
/// <see cref="PackageModel.Services"/> and emits one row per service whose
/// <see cref="ServiceModel.FailureActions"/> is populated (fluent
/// <c>ServiceBuilder.FailureActions(...)</c>). Before this producer existed,
/// <see cref="ServiceFailureActionsModel"/> captured recovery/restart
/// configuration on the model but nothing in FalkForge.Compiler.Msi emitted
/// it — service recovery (restart-on-crash) was silently lost (issue C3).
///
/// <see cref="ServiceFailureActionsModel"/> exposes three failure-tier
/// actions (first/second/subsequent) and a single restart delay shared
/// across all three tiers. <c>Actions</c>/<c>DelayActions</c> are
/// <c>[~]</c>-delimited parallel lists in that tier order — the same
/// multi-value convention <see cref="ServiceInstallTableProducer"/> uses for
/// <c>Dependencies</c>. A <see cref="FailureAction.None"/> tier always
/// carries a <c>0</c> delay regardless of the configured
/// <see cref="ServiceFailureActionsModel.RestartDelay"/>, since there is no
/// action to delay.
///
/// <c>Event</c> is a fixed bitmask of 5 (install=1 | reinstall=4): the
/// fluent builder does not expose per-lifecycle-event control, and applying
/// the failure-action configuration whenever the service is installed or
/// reinstalled — but not when it is being uninstalled — is the only
/// interpretation consistent with authoring a <c>.FailureActions(...)</c>
/// block at all. A future builder enhancement could expose the bitmask
/// directly; until then this fixed value is the documented default.
///
/// No sequencing is added: rows in this table are consumed automatically by
/// the standard <c>InstallServices</c> action alongside <c>ServiceInstall</c>
/// rows, so emitting the table (already unconditionally sequenced whenever a
/// package has services) is sufficient — there is no separate deferred
/// custom action to schedule.
/// </summary>
internal sealed class MsiServiceConfigFailureActionsTableProducer : ITableProducer
{
    private const string ListSeparator = "[~]";
    private const int EventInstallAndReinstall = 1 | 4;
    private const int FailureTierCount = 3;

    /// <summary>Static schema describing the <c>MsiServiceConfigFailureActions</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = WellKnownTableIds.Component;
        ResolvedPackage resolved = context.Resolved;

        Dictionary<string, string> fileNameToComponent = ServiceIdentity.BuildFileNameLookup(resolved);
        string defaultComponentId = ServiceIdentity.DefaultComponentId(resolved);

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        foreach (ServiceModel service in resolved.Package.Services)
        {
            if (service.FailureActions is not { } failureActions)
            {
                continue;
            }

            // ResetPeriod and RestartDelay are TimeSpan on the model but MSI's ResetPeriod
            // (seconds) and DelayActions (milliseconds) columns are 32-bit. Fail loud on a
            // value that would silently wrap to a negative/garbage duration rather than
            // writing a corrupt row — consistent with the "fail loud" working principle.
            Result<int> resetPeriodSeconds = ToInt32Duration(
                failureActions.ResetPeriod.TotalSeconds, service.Name, "ResetPeriod", "seconds");
            if (resetPeriodSeconds.IsFailure)
            {
                return Result<ImmutableArray<RecipeRow>>.Failure(resetPeriodSeconds.Error);
            }

            Result<(string Actions, string DelayActions)> encoded = EncodeActions(failureActions, service.Name);
            if (encoded.IsFailure)
            {
                return Result<ImmutableArray<RecipeRow>>.Failure(encoded.Error);
            }

            string componentId = ServiceIdentity.ResolveComponentId(service, resolved, fileNameToComponent, defaultComponentId);
            string rowId = ServiceIdentity.TruncateId($"SCF_{ServiceIdentity.SanitizeId(service.Name)}");
            (string actions, string delayActions) = encoded.Value;

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(rowId),
                new CellValue.StringValue(service.Name),
                new CellValue.IntValue(EventInstallAndReinstall),
                new CellValue.IntValue(resetPeriodSeconds.Value),
                failureActions.RebootMessage is null ? new CellValue.Null() : new CellValue.StringValue(failureActions.RebootMessage),
                failureActions.Command is null ? new CellValue.Null() : new CellValue.StringValue(failureActions.Command),
                new CellValue.StringValue(actions),
                new CellValue.StringValue(delayActions),
                new CellValue.ForeignKey(componentTable, componentId));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static Result<(string Actions, string DelayActions)> EncodeActions(
        ServiceFailureActionsModel model,
        string serviceName)
    {
        Span<FailureAction> tiers = [model.OnFirstFailure, model.OnSecondFailure, model.OnSubsequentFailures];

        Result<int> delayMs = ToInt32Duration(
            model.RestartDelay.TotalMilliseconds, serviceName, "RestartDelay", "milliseconds");
        if (delayMs.IsFailure)
        {
            return Result<(string, string)>.Failure(delayMs.Error);
        }

        string[] actionCodes = new string[FailureTierCount];
        string[] delayCodes = new string[FailureTierCount];

        for (int i = 0; i < tiers.Length; i++)
        {
            actionCodes[i] = MapAction(tiers[i]).ToString(CultureInfo.InvariantCulture);
            delayCodes[i] = (tiers[i] == FailureAction.None ? 0 : delayMs.Value).ToString(CultureInfo.InvariantCulture);
        }

        return Result<(string, string)>.Success(
            (string.Join(ListSeparator, actionCodes), string.Join(ListSeparator, delayCodes)));
    }

    private static Result<int> ToInt32Duration(double value, string serviceName, string field, string unit)
    {
        // The MSI column is a signed 32-bit integer; a negative or oversized duration cannot
        // be represented and would corrupt the row if cast silently.
        if (value < 0 || value > int.MaxValue)
        {
            return Result<int>.Failure(new Error(
                ErrorKind.CompilationError,
                $"Service '{serviceName}' {field} of {value:0} {unit} is out of range for the " +
                $"MSI MsiServiceConfigFailureActions table (0..{int.MaxValue} {unit})."));
        }

        return Result<int>.Success((int)value);
    }

    private static int MapAction(FailureAction action) => action switch
    {
        FailureAction.None => 0,
        FailureAction.Restart => 1,
        FailureAction.Reboot => 2,
        FailureAction.RunCommand => 3,
        _ => 0,
    };

    private static TableSchema BuildSchema()
    {
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("MsiServiceConfigFailureActions", 72),
            RecipeColumn.String("Name", 255),
            RecipeColumn.Integer("Event", 4),
            RecipeColumn.Integer("ResetPeriod", 4, nullable: true),
            RecipeColumn.String("RebootMessage", 255, nullable: true),
            RecipeColumn.String("Command", 255, nullable: true),
            RecipeColumn.String("Actions", 255, nullable: true),
            RecipeColumn.String("DelayActions", 255, nullable: true),
            RecipeColumn.String("Component_", 72));

        return new TableSchema
        {
            Name = WellKnownTableIds.MsiServiceConfigFailureActions,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(8),
                TargetTable = componentTable,
            }),
            // Opt-out of empty-table emission, matching LockPermissions/MsiLockPermissionsEx:
            // only create MsiServiceConfigFailureActions when at least one service configures
            // failure actions.
            EmitWhenEmpty = false,
        };
    }
}
