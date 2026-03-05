namespace FalkForge.Extensions.Util.QuietExec;

public sealed class QuietExecBuilder
{
    private string _commandLine = string.Empty;
    private string? _condition;
    private string _id = string.Empty;
    private string? _rollbackCommandLine;
    private string? _workingDirectory;

    public QuietExecBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public QuietExecBuilder Command(string commandLine)
    {
        _commandLine = commandLine;
        return this;
    }

    public QuietExecBuilder WorkingDir(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
        return this;
    }

    public QuietExecBuilder Condition(string condition)
    {
        _condition = condition;
        return this;
    }

    public QuietExecBuilder RollbackCommand(string rollbackCommandLine)
    {
        _rollbackCommandLine = rollbackCommandLine;
        return this;
    }

    internal Result<QuietExecModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_id))
            return Result<QuietExecModel>.Failure(ErrorKind.Validation, "QEX001: QuietExec Id is required.");

        if (string.IsNullOrWhiteSpace(_commandLine))
            return Result<QuietExecModel>.Failure(ErrorKind.Validation, "QEX002: QuietExec CommandLine is required.");

        const int maxCustomActionDataLength = 32767;
        if (_commandLine.Length > maxCustomActionDataLength)
            return Result<QuietExecModel>.Failure(ErrorKind.Validation,
                $"QEX003: QuietExec CommandLine exceeds maximum length of {maxCustomActionDataLength} characters.");

        if (_rollbackCommandLine is not null && _rollbackCommandLine.Length > maxCustomActionDataLength)
            return Result<QuietExecModel>.Failure(ErrorKind.Validation,
                $"QEX004: QuietExec RollbackCommandLine exceeds maximum length of {maxCustomActionDataLength} characters.");

        return new QuietExecModel
        {
            Id = _id,
            CommandLine = _commandLine,
            WorkingDirectory = _workingDirectory,
            Condition = _condition,
            RollbackCommandLine = _rollbackCommandLine
        };
    }
}