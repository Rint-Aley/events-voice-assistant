using Microsoft.EntityFrameworkCore;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Repositories;

public sealed class TurnRepository : ITurnRepository
{
    private static readonly TurnState[] ActiveStates =
    [
        TurnState.Transcribing,
        TurnState.Interpreting,
        TurnState.Applying,
    ];

    private readonly TaskManagerDbContext _dbContext;

    public TurnRepository(TaskManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Turn turn, CancellationToken cancellationToken = default)
    {
        await _dbContext.Turns.AddAsync(turn, cancellationToken);
    }

    public Task<Turn?> GetByIdAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        _dbContext.Turns
            .Include(turn => turn.Draft)
            .FirstOrDefaultAsync(turn => turn.Id == turnId, cancellationToken);

    public Task<Turn?> GetByIdempotencyKeyAsync(
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        _dbContext.Turns
            .Include(turn => turn.Draft)
            .FirstOrDefaultAsync(turn => turn.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<Turn?> GetActiveBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        _dbContext.Turns
            .Include(turn => turn.Draft)
            .FirstOrDefaultAsync(
                turn => turn.SessionId == sessionId && ActiveStates.Contains(turn.State),
                cancellationToken);

    public Task<Turn?> GetLatestClarificationAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Turns
            .Include(turn => turn.Draft)
            .Where(turn => turn.SessionId == sessionId
                && turn.State == TurnState.NeedsClarification
                && turn.Draft != null)
            .OrderByDescending(turn => turn.CreatedAt)
            .ThenByDescending(turn => turn.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
