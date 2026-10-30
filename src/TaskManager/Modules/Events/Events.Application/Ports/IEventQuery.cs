using TaskManager.Events.Application.Queries;
using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Ports;

/// <summary>
/// Чтение событий для списка и проверки конфликтов. Семантический поиск не объявлен:
/// он заблокирован до фиксации <c>EMBEDDING_MODEL</c> и <c>EMBEDDING_DIM</c>.
/// </summary>
public interface IEventQuery
{
    /// <summary>
    /// Список за период: <c>active</c> и <c>done</c>, без <c>cancelled</c>.
    /// <paramref name="to"/> включается: даты сравниваются как календарные дни.
    /// </summary>
    Task<EventListResult> ListPeriodAsync(
        DateOnly from,
        DateOnly to,
        EventTypeFilter? typeFilter,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Активные встречи, интервал <c>[start, start + duration)</c> которых пересекается
    /// с окном <c>[<paramref name="from"/>, <paramref name="to"/>)</c>. Нужен
    /// <c>ConflictDetector</c>: встреча могла начаться до окна и длиться через его начало.
    /// </summary>
    Task<IReadOnlyList<Event>> ListActiveMeetingsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}
