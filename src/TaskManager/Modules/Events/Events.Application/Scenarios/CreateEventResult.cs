using TaskManager.Events.Application.Dtos;

namespace TaskManager.Events.Application.Scenarios;

/// <summary>
/// Созданная запись и пересечения встреч. Конфликт — предупреждение, запись
/// сохраняется всегда.
/// </summary>
public sealed record CreateEventResult(
    EventDto Event,
    IReadOnlyList<EventDto> Conflicts);
