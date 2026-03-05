namespace FalkForge.Plugins.Odbc;

public interface IOdbcManager
{
    Result<bool> DsnExists(string dsnName);
    void LaunchOdbcAdministrator();
}