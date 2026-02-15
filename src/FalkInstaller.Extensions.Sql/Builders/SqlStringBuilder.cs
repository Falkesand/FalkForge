namespace FalkInstaller.Extensions.Sql.Builders;

using FalkInstaller.Extensions.Sql.Models;

public sealed class SqlStringBuilder
{
    private string _id = string.Empty;
    private string _databaseRef = string.Empty;
    private string _sql = string.Empty;
    private bool _executeOnInstall;
    private bool _executeOnUninstall;
    private int _sequence;
    private bool _continueOnError;

    public SqlStringBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public SqlStringBuilder Database(string databaseRef)
    {
        _databaseRef = databaseRef;
        return this;
    }

    public SqlStringBuilder Sql(string sql)
    {
        _sql = sql;
        return this;
    }

    public SqlStringBuilder ExecuteOnInstall(bool execute = true)
    {
        _executeOnInstall = execute;
        return this;
    }

    public SqlStringBuilder ExecuteOnUninstall(bool execute = true)
    {
        _executeOnUninstall = execute;
        return this;
    }

    public SqlStringBuilder Sequence(int sequence)
    {
        _sequence = sequence;
        return this;
    }

    public SqlStringBuilder ContinueOnError(bool continueOnError = true)
    {
        _continueOnError = continueOnError;
        return this;
    }

    public Result<SqlStringModel> Build()
    {
        var model = new SqlStringModel
        {
            Id = _id,
            DatabaseRef = _databaseRef,
            Sql = _sql,
            ExecuteOnInstall = _executeOnInstall,
            ExecuteOnUninstall = _executeOnUninstall,
            Sequence = _sequence,
            ContinueOnError = _continueOnError
        };

        var validationResult = SqlValidator.ValidateString(model);
        if (validationResult.IsFailure)
            return Result<SqlStringModel>.Failure(validationResult.Error);

        return model;
    }
}
