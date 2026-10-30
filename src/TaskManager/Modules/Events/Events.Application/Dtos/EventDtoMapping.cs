using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Dtos;

/// <summary>
/// Ручной маппинг Domain → DTO. Отдельный маппер не используется.
/// </summary>
public static class EventDtoMapping
{
    public static EventDto ToDto(this Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return new EventDto(
            @event.Id,
            @event.Kind,
            @event.Status,
            @event.Description,
            @event.SourceText,
            @event.StartsOn,
            @event.StartsAt,
            @event.Duration,
            @event.IsDeadline);
    }

    public static IReadOnlyList<EventDto> ToDtos(this IEnumerable<Event> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events.Select(ToDto).ToList();
    }
}
