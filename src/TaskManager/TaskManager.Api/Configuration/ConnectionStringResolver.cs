using Npgsql;

namespace TaskManager.Api.Configuration;

/// <summary>
/// Возвращает строку подключения Npgsql из конфигурации приложения. Поддерживаются
/// <c>ConnectionStrings:TaskManager</c>, <c>TASKMANAGER_CONNECTION_STRING</c> и
/// <c>DATABASE_URL</c> в форме <c>postgres://user:password@host:port/database</c>.
/// </summary>
public static class ConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? connectionString = configuration.GetConnectionString("TaskManager")
            ?? configuration["TASKMANAGER_CONNECTION_STRING"];

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string? databaseUrl = configuration["DATABASE_URL"];
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return FromDatabaseUrl(databaseUrl);
        }

        throw new InvalidOperationException(
            "Не задана строка подключения. Укажи ConnectionStrings:TaskManager, "
            + "TASKMANAGER_CONNECTION_STRING или DATABASE_URL.");
    }

    private static string FromDatabaseUrl(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            // Уже строка Npgsql в формате key=value.
            return value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            throw new InvalidOperationException("DATABASE_URL имеет неверный формат.");
        }

        string database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (string.IsNullOrWhiteSpace(uri.Host) || string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("DATABASE_URL должен содержать host и database.");
        }

        string[] credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = database,
            Username = credentials.Length > 0
                ? Uri.UnescapeDataString(credentials[0])
                : null,
            Password = credentials.Length > 1
                ? Uri.UnescapeDataString(credentials[1])
                : null,
        };

        return builder.ConnectionString;
    }
}
