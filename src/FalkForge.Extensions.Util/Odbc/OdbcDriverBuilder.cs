namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDriverBuilder
{
    private readonly string _id;
    private string _driverName = "";
    private string _fileName = "";
    private string? _setupFileName;

    public OdbcDriverBuilder(string id) => _id = id;

    public OdbcDriverBuilder DriverName(string name) { _driverName = name; return this; }

    /// <summary>
    /// Names the driver DLL. It must be a file the package already declares via <c>Files(...)</c>;
    /// the compiler derives the <c>ODBCDriver.File_</c> and <c>Component_</c> external keys from it.
    /// See <see cref="OdbcDriverModel.FileName"/>.
    /// </summary>
    public OdbcDriverBuilder FileName(string fileName) { _fileName = fileName; return this; }

    /// <summary>
    /// Names the optional driver setup DLL, resolved the same way as <see cref="FileName"/>.
    /// </summary>
    public OdbcDriverBuilder SetupFileName(string fileName) { _setupFileName = fileName; return this; }

    internal OdbcDriverModel Build() => new()
    {
        Id = _id,
        DriverName = _driverName,
        FileName = _fileName,
        SetupFileName = _setupFileName
    };
}
