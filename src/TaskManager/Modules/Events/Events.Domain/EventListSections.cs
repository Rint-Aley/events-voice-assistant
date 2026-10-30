namespace TaskManager.Events.Domain;

/// <summary>
/// Список записей, разложенный на датированные и блок «Без даты».
/// </summary>
public sealed record EventListSections(
    IReadOnlyList<Event> Dated,
    IReadOnlyList<Event> Undated);
