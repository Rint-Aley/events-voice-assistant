using Microsoft.EntityFrameworkCore;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Repositories;

public sealed class DomainCommandRepository : IDomainCommandRepository
{
    private readonly TaskManagerDbContext _dbContext;

    public DomainCommandRepository(TaskManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(
        DomainCommand command,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.DomainCommands.AddAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<DomainCommand>> ListPendingByTurnAsync(
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DomainCommands
            .Where(command => command.TurnId == turnId
                && command.Status == DomainCommandStatus.Pending)
            .OrderBy(command => command.Seq)
            .ThenBy(command => command.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DomainCommand>> ListByTurnAsync(
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DomainCommands
            .AsNoTracking()
            .Where(command => command.TurnId == turnId)
            .OrderBy(command => command.Seq)
            .ThenBy(command => command.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueTurnIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }

        // Тик обрабатывает ход отдельной транзакцией и блокирует строку turns.
        // Postgres не принимает DISTINCT с ORDER BY по колонкам вне выборки, поэтому
        // turn_id схлопывается через GROUP BY, а порядок задаёт самая ранняя команда хода.
        // Порядок команд внутри хода задаёт ListPendingByTurnAsync.
        return await _dbContext.DomainCommands
            .AsNoTracking()
            .Where(command => command.Status == DomainCommandStatus.Pending
                && command.NextAttemptAt <= now)
            .GroupBy(command => command.TurnId)
            .OrderBy(group => group.Min(command => command.NextAttemptAt))
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
