using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Запись ответа хода и его связей с событиями.
/// </summary>
public interface IAnswerRepository
{
    Task AddAsync(Answer answer, CancellationToken cancellationToken = default);

    Task AddEventAsync(AnswerEvent answerEvent, CancellationToken cancellationToken = default);

    Task<Answer?> GetByIdAsync(Guid answerId, CancellationToken cancellationToken = default);

    Task<Answer?> GetByTurnIdAsync(Guid turnId, CancellationToken cancellationToken = default);
}
