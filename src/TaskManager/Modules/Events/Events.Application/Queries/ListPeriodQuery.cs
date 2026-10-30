using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Queries;

/// <summary>
/// Список за период. Границы включительно, даты сравниваются по <c>starts_on</c>.
/// Если <c>To</c> раньше <c>From</c>, сценарий возвращает <c>INVALID_PERIOD</c>.
/// </summary>
public sealed record ListPeriodQuery(
    DateOnly From,
    DateOnly To,
    EventTypeFilter? TypeFilter = null);
