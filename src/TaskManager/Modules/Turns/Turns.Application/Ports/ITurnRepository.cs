using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Запись и выборка агрегата хода. Репозиторий не открывает и не коммитит транзакции.
/// </summary>
public interface ITurnRepository
{
    Task AddAsync(Turn turn, CancellationToken cancellationToken = default);

    Task<Turn?> GetByIdAsync(Guid turnId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает ход с блокировкой строки <c>FOR UPDATE</c> до конца транзакции сценария.
    /// Нужен сценарию применения команд: порядок <c>seq</c> и переход состояния
    /// защищены от параллельного применения того же хода.
    /// </summary>
    Task<Turn?> GetByIdForUpdateAsync(Guid turnId, CancellationToken cancellationToken = default);

    Task<Turn?> GetByIdempotencyKeyAsync(Guid idempotencyKey, CancellationToken cancellationToken = default);

    Task<Turn?> GetActiveBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Последний ход сессии в <see cref="TurnState.NeedsClarification"/> вместе с черновиком.
    /// Черновик предыдущего уточнения — источник слотов для следующего запроса.
    /// </summary>
    Task<Turn?> GetLatestClarificationAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Активные ходы (<c>TRANSCRIBING</c>, <c>INTERPRETING</c>, <c>APPLYING</c>) без
    /// отслеживания изменений. Нужны <c>reconciler</c>: просроченные ходы и пропавшие задачи.
    /// </summary>
    Task<IReadOnlyList<Turn>> ListActiveAsync(CancellationToken cancellationToken = default);
}
