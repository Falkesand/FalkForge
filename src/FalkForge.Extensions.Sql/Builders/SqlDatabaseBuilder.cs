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
    private string? _user;
    private string? _password;
    private string? _passwordProperty;

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

    /// <summary>
    /// Sets the SQL Server login name for SQL authentication. Combine with <see cref="PasswordProperty"/>
    /// (secure, recommended) or <see cref="Password"/> (literal, discouraged). Omit entirely for Windows
    /// integrated authentication.
    /// </summary>
    public SqlDatabaseBuilder User(string user)
    {
        _user = user;
        return this;
    }

    /// <summary>
    /// Supplies the SQL-authentication password securely via the named MSI property, populated at run time
    /// through <c>IInstallerEngine.SetSecureProperty</c>. The password is never stored in the MSI. This is
    /// the recommended path; mutually exclusive with <see cref="Password"/>.
    /// <para>
    /// <b>Runtime exposure (honest limitations).</b> The password reaches the deferred custom action as
    /// <c>CustomActionData</c>. The SQL extension automatically adds the carrying properties to
    /// <c>MsiHiddenProperties</c> so their values are redacted from a verbose MSI log. Two residual
    /// exposures remain, inherent to running the work via <c>powershell.exe</c> (an EXE custom action): the
    /// resolved value is passed as a process command-line argument, so it is briefly visible to a local
    /// process listing while the action runs; and the property name must be a public (uppercase) MSI
    /// property. The value must also not contain a double-quote character (the command-line transport is
    /// double-quoted). For a secret that cannot meet these constraints, prefer Windows integrated
    /// authentication.
    /// </para>
    /// </summary>
    public SqlDatabaseBuilder PasswordProperty(string propertyName)
    {
        _passwordProperty = propertyName;
        return this;
    }

    /// <summary>
    /// Supplies a literal SQL-authentication password. <b>Discouraged</b> — the literal is embedded in
    /// plaintext in the compiled MSI (SQL015 warning). Prefer <see cref="PasswordProperty"/> or integrated
    /// authentication. Mutually exclusive with <see cref="PasswordProperty"/>.
    /// </summary>
    public SqlDatabaseBuilder Password(string password)
    {
        _password = password;
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
            ComponentRef = _componentRef,
            User = _user,
            PasswordProperty = _passwordProperty,
            Password = _password
        };

        var validationResult = SqlValidator.ValidateDatabase(model);
        if (validationResult.IsFailure)
            return Result<SqlDatabaseModel>.Failure(validationResult.Error);

        // SQL014: non-blocking warning — emit to stderr so the developer sees it but compilation continues.
        if (!string.IsNullOrEmpty(_connectionString))
        {
            var credCheck = SqlValidator.CheckConnectionStringCredentials(_connectionString);
            if (credCheck.IsFailure)
                Console.Error.WriteLine($"[FalkForge Warning] {credCheck.Error.Message}");
        }

        // SQL015: non-blocking warning — a literal password is embedded in plaintext in the MSI. Mirrors
        // the REG007/CTB011 posture: allowed, but the author is steered to PasswordProperty/integrated auth.
        if (SqlValidator.HasLiteralPassword(model))
            Console.Error.WriteLine(
                "[FalkForge Warning] SQL015: A literal SQL password is embedded in plaintext in the MSI. " +
                "Use PasswordProperty with SetSecureProperty, or Windows integrated authentication, instead.");

        return model;
    }
}