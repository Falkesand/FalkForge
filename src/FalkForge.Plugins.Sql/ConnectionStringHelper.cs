using Microsoft.Data.SqlClient;

namespace FalkForge.Plugins.Sql;

internal static class ConnectionStringHelper
{
    public static string Build(string server, string? database, bool integratedSecurity,
        string? userName, string? password, int timeoutSeconds = 5,
        bool encrypt = true, bool trustServerCertificate = false)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            IntegratedSecurity = integratedSecurity,
            ConnectTimeout = timeoutSeconds,
            TrustServerCertificate = trustServerCertificate,
            Encrypt = encrypt
                ? SqlConnectionEncryptOption.Mandatory
                : SqlConnectionEncryptOption.Optional
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