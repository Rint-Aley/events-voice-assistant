using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Dtos;

/// <summary>
/// Запись для ответа сценария. Не содержит вычисляемых полей Domain: их собирает
/// слой API при формировании контракта.
/// </summary>
public sealed record EventDto(
    Guid Id,
    EventKind Kind,
    EventStatus Status,
    string Description,
    string? SourceText,
    DateOnly? StartsOn,
    TimeOnly? StartsAt,
    TimeSpan? Duration,
    bool IsDeadline);
