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
}
