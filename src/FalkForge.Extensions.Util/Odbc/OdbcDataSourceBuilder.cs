namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDataSourceBuilder
{
    private readonly string _id;
    private string _name = "";
    private string _driverName = "";
    private OdbcRegistration _registration = OdbcRegistration.PerMachine;
    private readonly Dictionary<string, string> _properties = new();

    public OdbcDataSourceBuilder(string id) => _id = id;

    public OdbcDataSourceBuilder Name(string name) { _name = name; return this; }
    public OdbcDataSourceBuilder DriverName(string driver) { _driverName = driver; return this; }
    public OdbcDataSourceBuilder Registration(OdbcRegistration reg) { _registration = reg; return this; }
    public OdbcDataSourceBuilder Property(string key, string value) { _properties[key] = value; return this; }

    internal OdbcDataSourceModel Build() => new()
    {
        Id = _id,
        Name = _name,
        DriverName = _driverName,
        Registration = _registration,
        Properties = new Dictionary<string, string>(_properties)
    };
}
