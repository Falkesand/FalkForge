namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDriverBuilder
{
    private readonly string _id;
    private string _driverName = "";
    private string _fileName = "";
    private string? _setupFileName;

    public OdbcDriverBuilder(string id) => _id = id;

    public OdbcDriverBuilder DriverName(string name) { _driverName = name; return this; }
    public OdbcDriverBuilder FileName(string fileName) { _fileName = fileName; return this; }
    public OdbcDriverBuilder SetupFileName(string fileName) { _setupFileName = fileName; return this; }

    internal OdbcDriverModel Build() => new()
    {
        Id = _id,
        DriverName = _driverName,
        FileName = _fileName,
        SetupFileName = _setupFileName
    };
}
