using TaskManager.Events.Application.Dtos;

namespace TaskManager.Events.Application.Queries;

/// <summary>
/// Список за период: до трёх записей, общее количество до лимита и блоки
/// «датированные» и «Без даты».
/// </summary>
/// <remarks>
/// <see cref="Events"/> — конкатенация <see cref="Dated"/> и <see cref="Undated"/>
/// в порядке списка; сценарий собирает её из тех же блоков.
/// </remarks>
public sealed record ListPeriodResult(
    IReadOnlyList<EventDto> Events,
    int TotalCount,
    IReadOnlyList<EventDto> Dated,
    IReadOnlyList<EventDto> Undated);
