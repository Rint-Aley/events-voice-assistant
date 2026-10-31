using TaskManager.Turns.Application.Queries;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Чтение снапшота хода для polling и SSE. Не участвует в записи.
/// </summary>
public interface ITurnQuery
{
    Task<TurnSnapshot?> GetSnapshotAsync(Guid turnId, CancellationToken cancellationToken = default);
}
