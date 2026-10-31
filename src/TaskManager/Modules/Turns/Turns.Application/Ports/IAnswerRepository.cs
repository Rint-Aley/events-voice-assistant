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

    /// <summary>
    /// Читает ответ с блокировкой строки. Нужен сценариям, которые меняют
    /// <c>audio_status</c>: поздний провал <c>tts</c> не должен затирать готовое аудио.
    /// </summary>
    Task<Answer?> GetByIdForUpdateAsync(Guid answerId, CancellationToken cancellationToken = default);

    Task<Answer?> GetByTurnIdAsync(Guid turnId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ответы с ожидающей озвучкой, созданные раньше <paramref name="createdBefore"/>.
    /// Нужны <c>reconciler</c>: зависшая озвучка без задачи помечается <c>failed</c>.
    /// </summary>
    Task<IReadOnlyList<Answer>> ListPendingCreatedBeforeAsync(
        DateTimeOffset createdBefore,
        CancellationToken cancellationToken = default);
}
