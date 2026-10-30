using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Запись и выборка агрегата хода. Репозиторий не открывает и не коммитит транзакции.
/// </summary>
public interface ITurnRepository
{
    Task AddAsync(Turn turn, CancellationToken cancellationToken = default);

    Task<Turn?> GetByIdAsync(Guid turnId, CancellationToken cancellationToken = default);

    Task<Turn?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<Turn?> GetActiveBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Последний ход сессии в <see cref="TurnState.NeedsClarification"/> вместе с черновиком.
    /// Черновик предыдущего уточнения — источник слотов для следующего запроса.
    /// </summary>
    Task<Turn?> GetLatestClarificationAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
