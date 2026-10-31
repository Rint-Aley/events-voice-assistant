using Microsoft.EntityFrameworkCore;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Repositories;

public sealed class DraftRepository : IDraftRepository
{
    private readonly TaskManagerDbContext _dbContext;

    public DraftRepository(TaskManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Draft draft, CancellationToken cancellationToken = default)
    {
        await _dbContext.Drafts.AddAsync(draft, cancellationToken);
    }

    public Task<Draft?> GetByTurnIdAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        _dbContext.Drafts.FirstOrDefaultAsync(draft => draft.TurnId == turnId, cancellationToken);

    /// <summary>
    /// Помечает черновик на удаление. EF удаляет синхронно и без I/O, поэтому
    /// <paramref name="cancellationToken"/> не используется: отмена здесь неприменима.
    /// </summary>
    public Task RemoveAsync(Draft draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.Drafts.Remove(draft);
        return Task.CompletedTask;
    }
}
