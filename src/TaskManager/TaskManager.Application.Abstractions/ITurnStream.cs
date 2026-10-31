namespace TaskManager.Application.Abstractions;

/// <summary>
/// Шина событий хода для SSE. Реализация — in-memory hub одной реплики.
/// </summary>
public interface ITurnStream
{
    /// <summary>
    /// Публикует событие подписчикам хода. Вызывается после коммита транзакции.
    /// </summary>
    ValueTask PublishAsync(
        TurnStreamEvent streamEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подписывается на живые события хода. Снапшот при подключении отдаёт Api,
    /// а не порт.
    /// </summary>
    IAsyncEnumerable<TurnStreamEvent> SubscribeAsync(
        Guid turnId,
        CancellationToken cancellationToken = default);
}
