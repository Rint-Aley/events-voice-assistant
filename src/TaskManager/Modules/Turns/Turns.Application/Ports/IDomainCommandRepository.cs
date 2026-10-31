using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Запись и выборка команд домена. Репозиторий не открывает и не коммитит транзакции.
/// </summary>
public interface IDomainCommandRepository
{
    Task AddAsync(DomainCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pending-команды хода в порядке применения. Вызывается под блокировкой хода,
    /// поэтому порядок <c>seq</c> не нарушается параллельной записью.
    /// </summary>
    Task<IReadOnlyList<DomainCommand>> ListPendingByTurnAsync(
        Guid turnId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Все команды хода в порядке <c>seq</c>, включая применённые и проваленные.
    /// Нужны <c>reconciler</c> для страховки <c>APPLYING</c>.
    /// </summary>
    Task<IReadOnlyList<DomainCommand>> ListByTurnAsync(
        Guid turnId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ходы, у которых есть команда, готовая к применению. Тик <c>apply</c> обрабатывает
    /// каждый ход отдельной транзакцией, поэтому выбираются только идентификаторы.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueTurnIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);
}
