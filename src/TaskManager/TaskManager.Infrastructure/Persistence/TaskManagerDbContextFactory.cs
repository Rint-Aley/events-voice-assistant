using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskManager.Infrastructure.Persistence;

/// <summary>
/// Контекст для <c>dotnet ef</c>. Строка подключения не хранится в коде приложения.
/// </summary>
public sealed class TaskManagerDbContextFactory : IDesignTimeDbContextFactory<TaskManagerDbContext>
{
    public TaskManagerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TASKMANAGER_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Не задана переменная окружения TASKMANAGER_CONNECTION_STRING для design-time операций EF.");
        }

        var options = new DbContextOptionsBuilder<TaskManagerDbContext>()
            .UseTaskManagerPostgres(connectionString)
            .Options;

        return new TaskManagerDbContext(options);
    }
}
