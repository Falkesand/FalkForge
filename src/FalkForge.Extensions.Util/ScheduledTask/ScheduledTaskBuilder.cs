namespace FalkForge.Extensions.Util.ScheduledTask;

public sealed class ScheduledTaskBuilder
{
    private readonly string _id;
    private string _name = "";
    private string _command = "";
    private string? _arguments;
    private string? _workingDirectory;
    private ScheduledTaskTriggerType _triggerType = ScheduledTaskTriggerType.OnInstall;
    private string? _schedule;
    private string? _runAsUser;
    private bool _runElevated;

    public ScheduledTaskBuilder(string id) => _id = id;

    public ScheduledTaskBuilder Name(string name) { _name = name; return this; }
    public ScheduledTaskBuilder Command(string command) { _command = command; return this; }
    public ScheduledTaskBuilder Arguments(string arguments) { _arguments = arguments; return this; }
    public ScheduledTaskBuilder WorkingDirectory(string dir) { _workingDirectory = dir; return this; }
    public ScheduledTaskBuilder TriggerOnInstall() { _triggerType = ScheduledTaskTriggerType.OnInstall; return this; }
    public ScheduledTaskBuilder TriggerOnLogin() { _triggerType = ScheduledTaskTriggerType.OnLogin; return this; }
    public ScheduledTaskBuilder TriggerOnSchedule(string schedule) { _triggerType = ScheduledTaskTriggerType.OnSchedule; _schedule = schedule; return this; }
    public ScheduledTaskBuilder TriggerOnBoot() { _triggerType = ScheduledTaskTriggerType.OnBoot; return this; }
    public ScheduledTaskBuilder RunAsUser(string user) { _runAsUser = user; return this; }
    public ScheduledTaskBuilder RunElevated(bool elevated = true) { _runElevated = elevated; return this; }

    internal ScheduledTaskModel Build() => new()
    {
        Id = _id,
        Name = _name,
        Command = _command,
        Arguments = _arguments,
        WorkingDirectory = _workingDirectory,
        TriggerType = _triggerType,
        Schedule = _schedule,
        RunAsUser = _runAsUser,
        RunElevated = _runElevated
    };
}
