namespace FalkForge.Plugins.Sql;

using Microsoft.Data.SqlClient;

internal static class ConnectionStringHelper
{
    public static string Build(string server, string? database, bool integratedSecurity,
        string? userName, string? password, int timeoutSeconds = 5)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            IntegratedSecurity = integratedSecurity,
            ConnectTimeout = timeoutSeconds,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
        };
        if (!string.IsNullOrEmpty(database))
            builder.InitialCatalog = database;
        if (!integratedSecurity)
        {
            builder.UserID = userName ?? string.Empty;
            builder.Password = password ?? string.Empty;
        }
        return builder.ConnectionString;
    }
}
