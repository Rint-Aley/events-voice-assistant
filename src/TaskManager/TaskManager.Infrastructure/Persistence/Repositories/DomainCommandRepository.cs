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
}
