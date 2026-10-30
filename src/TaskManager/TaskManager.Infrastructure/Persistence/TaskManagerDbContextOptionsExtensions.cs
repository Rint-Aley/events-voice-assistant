using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace TaskManager.Infrastructure.Persistence;

/// <summary>
/// Единая настройка Npgsql для контекста: без <c>UseVector</c> тип <c>vector</c> не читается.
/// </summary>
public static class TaskManagerDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder UseTaskManagerPostgres(
        this DbContextOptionsBuilder builder,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseNpgsql(connectionString, npgsql => npgsql.UseVector());
    }

    public static DbContextOptionsBuilder<TContext> UseTaskManagerPostgres<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string connectionString)
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseNpgsql(connectionString, npgsql => npgsql.UseVector());
    }
}
