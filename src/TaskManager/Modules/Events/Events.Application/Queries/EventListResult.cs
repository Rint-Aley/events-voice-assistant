using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Queries;

/// <summary>
/// Результат списка: до лимита записей и общее количество до применения лимита.
/// </summary>
public sealed record EventListResult(
    IReadOnlyList<Event> Events,
    int TotalCount);
