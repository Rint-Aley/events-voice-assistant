using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Ports;

/// <summary>
/// Запись агрегата события. Репозиторий не открывает и не коммитит транзакции.
/// </summary>
public interface IEventRepository
{
    Task AddAsync(Event @event, CancellationToken cancellationToken = default);

    Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default);
}
