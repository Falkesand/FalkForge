using FalkForge.Extensions.Sql.Models;

namespace FalkForge.Extensions.Sql.Builders;

public sealed class SqlDatabaseBuilder
{
    private string? _componentRef;
    private bool _confirmOverwrite;
    private string? _connectionString;
    private bool _createOnInstall;
    private string _database = string.Empty;
    private bool _dropOnUninstall;
    private string _id = string.Empty;
    private string? _instance;
    private string? _server;

    public SqlDatabaseBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public SqlDatabaseBuilder Server(string server)
    {
        _server = server;
        return this;
    }

    public SqlDatabaseBuilder Database(string database)
    {
        _database = database;
        return this;
    }

    public SqlDatabaseBuilder Instance(string instance)
    {
        _instance = instance;
        return this;
    }

    public SqlDatabaseBuilder ConnectionString(string connectionString)
    {
        _connectionString = connectionString;
        return this;
    }

    public SqlDatabaseBuilder CreateOnInstall(bool create = true)
    {
        _createOnInstall = create;
        return this;
    }

    public SqlDatabaseBuilder DropOnUninstall(bool drop = true)
    {
        _dropOnUninstall = drop;
        return this;
    }

    public SqlDatabaseBuilder ConfirmOverwrite(bool confirm = true)
    {
        _confirmOverwrite = confirm;
        return this;
    }

    public SqlDatabaseBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    public Result<SqlDatabaseModel> Build()
    {
        var model = new SqlDatabaseModel
        {
            Id = _id,
            Server = _server,
            Database = _database,
            Instance = _instance,
            ConnectionString = _connectionString,
            CreateOnInstall = _createOnInstall,
            DropOnUninstall = _dropOnUninstall,
            ConfirmOverwrite = _confirmOverwrite,
            ComponentRef = _componentRef
        };

        var validationResult = SqlValidator.ValidateDatabase(model);
        if (validationResult.IsFailure)
            return Result<SqlDatabaseModel>.Failure(validationResult.Error);

        return model;
    }
}