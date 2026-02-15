namespace FalkInstaller.Extensions.Sql.Builders;

using FalkInstaller.Extensions.Sql.Models;

public sealed class SqlScriptBuilder
{
    private string _id = string.Empty;
    private string _databaseRef = string.Empty;
    private string? _sourceFile;
    private string? _sqlContent;
    private bool _executeOnInstall;
    private bool _executeOnReinstall;
    private bool _executeOnUninstall;
    private string? _rollbackSourceFile;
    private int _sequence;
    private bool _continueOnError;
    private string? _componentRef;

    public SqlScriptBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public SqlScriptBuilder Database(string databaseRef)
    {
        _databaseRef = databaseRef;
        return this;
    }

    public SqlScriptBuilder SourceFile(string sourceFile)
    {
        _sourceFile = sourceFile;
        return this;
    }

    public SqlScriptBuilder InlineSql(string sqlContent)
    {
        _sqlContent = sqlContent;
        return this;
    }

    public SqlScriptBuilder ExecuteOnInstall(bool execute = true)
    {
        _executeOnInstall = execute;
        return this;
    }

    public SqlScriptBuilder ExecuteOnReinstall(bool execute = true)
    {
        _executeOnReinstall = execute;
        return this;
    }

    public SqlScriptBuilder ExecuteOnUninstall(bool execute = true)
    {
        _executeOnUninstall = execute;
        return this;
    }

    public SqlScriptBuilder RollbackScript(string rollbackSourceFile)
    {
        _rollbackSourceFile = rollbackSourceFile;
        return this;
    }

    public SqlScriptBuilder Sequence(int sequence)
    {
        _sequence = sequence;
        return this;
    }

    public SqlScriptBuilder ContinueOnError(bool continueOnError = true)
    {
        _continueOnError = continueOnError;
        return this;
    }

    public SqlScriptBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    public Result<SqlScriptModel> Build()
    {
        var model = new SqlScriptModel
        {
            Id = _id,
            DatabaseRef = _databaseRef,
            SourceFile = _sourceFile,
            SqlContent = _sqlContent,
            ExecuteOnInstall = _executeOnInstall,
            ExecuteOnReinstall = _executeOnReinstall,
            ExecuteOnUninstall = _executeOnUninstall,
            RollbackSourceFile = _rollbackSourceFile,
            Sequence = _sequence,
            ContinueOnError = _continueOnError,
            ComponentRef = _componentRef
        };

        var validationResult = SqlValidator.ValidateScript(model);
        if (validationResult.IsFailure)
            return Result<SqlScriptModel>.Failure(validationResult.Error);

        return model;
    }
}
