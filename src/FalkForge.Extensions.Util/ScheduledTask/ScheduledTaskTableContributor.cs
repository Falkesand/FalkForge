using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.ScheduledTask;

public sealed class ScheduledTaskTableContributor : IMsiTableContributor
{
    private readonly List<ScheduledTaskModel> _tasks = [];

    public string TableName => "FalkForgeScheduledTask";

    /// <inheritdoc/>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("Id"),
        ContributedColumn.Text("Name"),
        ContributedColumn.Text("Command"),
        ContributedColumn.Text("Arguments"),
        ContributedColumn.Text("WorkingDirectory"),
        ContributedColumn.Int("TriggerType"),
        ContributedColumn.Text("Schedule"),
        ContributedColumn.Text("RunAsUser"),
        ContributedColumn.Int("RunElevated"),
    ];

    public void Add(ScheduledTaskModel task) => _tasks.Add(task);

    public IReadOnlyList<ScheduledTaskModel> Tasks => _tasks;

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_tasks.Count);

        foreach (var task in _tasks)
        {
            var row = new MsiTableRow()
                .Set("Id", task.Id)
                .Set("Name", task.Name)
                .Set("Command", task.Command)
                .Set("Arguments", task.Arguments)
                .Set("WorkingDirectory", task.WorkingDirectory)
                .Set("TriggerType", (int)task.TriggerType)
                .Set("Schedule", task.Schedule)
                .Set("RunAsUser", task.RunAsUser)
                .Set("RunElevated", task.RunElevated ? 1 : 0);

            rows.Add(row);
        }

        return rows;
    }
}
