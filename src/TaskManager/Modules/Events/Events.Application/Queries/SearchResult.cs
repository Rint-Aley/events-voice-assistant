using TaskManager.Events.Application.Dtos;
using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Queries;

/// <summary>
/// Результат семантического поиска: до трёх ближайших записей в порядке убывания
/// близости. Пустой список — успешный пустой результат, а не ошибка.
/// </summary>
public sealed record SearchResult(IReadOnlyList<EventDto> Events)
{
    /// <summary>Поиск ничего не нашёл: ход завершается <c>COMPLETED</c> с пустым списком.</summary>
    public bool IsEmpty => Events.Count == 0;
}
