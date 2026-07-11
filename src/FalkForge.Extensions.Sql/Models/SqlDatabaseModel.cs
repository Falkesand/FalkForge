namespace FalkForge.Extensions.Sql.Models;

public sealed class SqlDatabaseModel
{
    public required string Id { get; init; }
    public string? Server { get; init; }
    public required string Database { get; init; }
    public string? Instance { get; init; }
    public string? ConnectionString { get; init; }
    public bool CreateOnInstall { get; init; }
    public bool DropOnUninstall { get; init; }
    public bool ConfirmOverwrite { get; init; }
    public string? ComponentRef { get; init; }

    /// <summary>
    /// SQL Server login name for SQL authentication. When <see langword="null"/>/empty the install-time
    /// custom action connects with Windows integrated authentication (<c>Integrated Security=SSPI</c>) as
    /// the account the deferred action runs under (SYSTEM for a per-machine install).
    /// </summary>
    public string? User { get; init; }

    /// <summary>
    /// Name of an MSI property that supplies the SQL-authentication password <b>at run time</b> — the
    /// secure, recommended path. The property value is never stored in the MSI: the execution seam emits
    /// an immediate <c>SetProperty</c> (type 51) custom action that copies <c>[PasswordProperty]</c> into
    /// the deferred action's <c>CustomActionData</c>, and the value is supplied at run time via
    /// <c>IInstallerEngine.SetSecureProperty</c>. Mutually exclusive with <see cref="Password"/>.
    /// </summary>
    public string? PasswordProperty { get; init; }

    /// <summary>
    /// Literal SQL-authentication password. <b>Discouraged</b>: a literal here is embedded in plaintext in
    /// the compiled MSI (SQL015 warning), mirroring the REG007/CTB011 posture. Prefer
    /// <see cref="PasswordProperty"/> with <c>SetSecureProperty</c>, or Windows integrated authentication.
    /// Mutually exclusive with <see cref="PasswordProperty"/>.
    /// </summary>
    public string? Password { get; init; }
}
