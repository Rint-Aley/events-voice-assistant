using Microsoft.EntityFrameworkCore;
using TaskManager.Events.Domain;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence;

/// <summary>
/// Единственный контекст TaskManager и единственная история миграций.
/// </summary>
public sealed class TaskManagerDbContext : DbContext
{
    public TaskManagerDbContext(DbContextOptions<TaskManagerDbContext> options)
        : base(options)
    {
    }

    public DbSet<Turn> Turns => Set<Turn>();

    public DbSet<Draft> Drafts => Set<Draft>();

    public DbSet<Answer> Answers => Set<Answer>();

    public DbSet<AnswerEvent> AnswerEvents => Set<AnswerEvent>();

    public DbSet<DomainCommand> DomainCommands => Set<DomainCommand>();

    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TaskManagerDbContext).Assembly);
    }
}
