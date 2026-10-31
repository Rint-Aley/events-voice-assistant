using TaskManager.Events.Application.Queries;
using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Ports;

/// <summary>
/// Чтение событий для списка, поиска и проверки конфликтов.
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
    /// Семантический поиск по косинусной близости <c>description</c>: только
    /// <c>active</c> и <c>done</c>, без <c>cancelled</c>. Возвращает до
    /// <paramref name="limit"/> записей в порядке убывания близости.
    /// </summary>
    /// <param name="query">Вектор запроса, фильтр типа и необязательный период.</param>
    /// <param name="limit">Максимальное число записей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<Event>> SearchAsync(
        SearchQuery query,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает активные встречи-кандидаты, которые могли пересечься с окном
    /// <c>[<paramref name="from"/>, <paramref name="to"/>)</c>. Это надмножество: в него
    /// попадают встречи, начавшиеся за сутки до окна и позже, но не гарантируется, что
    /// интервал каждой из них пересекает окно. Точную проверку с учётом
    /// <c>[start, start + duration)</c> делает <c>ConflictDetector</c>.
    /// </summary>
    /// <remarks>
    /// Суточный запас достаточен, потому что <c>duration_minutes</c> в контракте NLU не
    /// превышает 1440. Встречи, созданные дольше чем на сутки в обход NLU, в кандидаты
    /// не попадут. Если <paramref name="to"/> не позже <paramref name="from"/>, возвращается
    /// пустой список.
    /// </remarks>
    Task<IReadOnlyList<Event>> ListActiveMeetingCandidatesAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}
