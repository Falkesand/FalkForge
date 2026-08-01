namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDriverModel
{
    public required string Id { get; init; }
    public required string DriverName { get; init; }

    /// <summary>
    /// Reference to the driver DLL, which must already be declared in the package via
    /// <c>Files(...)</c>. Either the bare file name (<c>mydriver.dll</c>) or a trailing part of its
    /// install path when two declared files share the name (<c>x64/mydriver.dll</c>). The compiler
    /// derives both <c>ODBCDriver.File_</c> and <c>ODBCDriver.Component_</c> from it; a reference
    /// that matches no declared file — or more than one — fails the build.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Optional reference to the driver's setup DLL, resolved exactly like <see cref="FileName"/>
    /// and emitted as <c>ODBCDriver.File_Setup</c>. Authored but unresolvable fails the build
    /// rather than silently dropping the column.
    /// </summary>
    public string? SetupFileName { get; init; }
}
