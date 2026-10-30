namespace TaskManager.Events.Domain;

/// <summary>Правила поиска и списка: видимость, type_filter и порядок записей.</summary>
public static class EventSelection
{
    /// <summary>
    /// Поиск и список показывают <see cref="EventStatus.Active"/> и
    /// <see cref="EventStatus.Done"/>; <see cref="EventStatus.Cancelled"/> исключён.
    /// </summary>
    public static bool IsVisible(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return @event.IsVisible;
    }

    /// <summary>
    /// Проверяет фильтр типа: meeting — встречи, task — задачи и напоминания,
    /// reminder — только напоминания, отсутствие фильтра — все типы.
    /// </summary>
    public static bool MatchesTypeFilter(Event @event, EventTypeFilter? typeFilter)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return typeFilter switch
        {
            null => true,
            EventTypeFilter.Meeting => @event.Kind == EventKind.Meeting,
            EventTypeFilter.Task => @event.Kind is EventKind.Task or EventKind.Reminder,
            EventTypeFilter.Reminder => @event.Kind == EventKind.Reminder,
            _ => false,
        };
    }

    /// <summary>Фильтрует записи для поиска: видимые и подходящие под type_filter.</summary>
    public static IReadOnlyList<Event> Filter(
        IEnumerable<Event> events,
        EventTypeFilter? typeFilter = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events
            .Where(IsVisible)
            .Where(@event => MatchesTypeFilter(@event, typeFilter))
            .ToList();
    }

    /// <summary>
    /// Сортирует список: сначала по <see cref="Event.StartsOn"/>, затем по
    /// <see cref="Event.StartsAt"/>; записи без времени идут в конце своего дня,
    /// задачи без даты — после всех датированных.
    /// </summary>
    public static IReadOnlyList<Event> SortForList(IEnumerable<Event> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events
            .OrderBy(@event => @event.StartsOn.HasValue ? 0 : 1)
            .ThenBy(@event => @event.StartsOn)
            .ThenBy(@event => @event.StartsAt.HasValue ? 0 : 1)
            .ThenBy(@event => @event.StartsAt)
            .ToList();
    }

    /// <summary>
    /// Готовит блоки списка: датированные записи в порядке списка и задачи без даты
    /// отдельным блоком «Без даты». Отменённые записи исключаются.
    /// </summary>
    public static EventListSections ListSections(
        IEnumerable<Event> events,
        EventTypeFilter? typeFilter = null)
    {
        IReadOnlyList<Event> sorted = SortForList(Filter(events, typeFilter));

        return new EventListSections(
            sorted.Where(@event => @event.StartsOn.HasValue).ToList(),
            sorted.Where(@event => !@event.StartsOn.HasValue).ToList());
    }
}
