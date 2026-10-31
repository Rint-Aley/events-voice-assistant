using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Запись и выборка черновиков. Черновик уникален по ходу.
/// </summary>
public interface IDraftRepository
{
    Task AddAsync(Draft draft, CancellationToken cancellationToken = default);

    Task<Draft?> GetByTurnIdAsync(Guid turnId, CancellationToken cancellationToken = default);

    Task RemoveAsync(Draft draft, CancellationToken cancellationToken = default);
}
