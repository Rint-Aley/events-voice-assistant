using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Commands;

/// <summary>
/// Запрос на создание записи. Слоты необязательны на уровне команды: какие из них
/// действительно нужны, решают Domain-правила для конкретного типа.
/// </summary>
public sealed record CreateEventCommand(
    EventKind Kind,
    string? Description,
    string? SourceText = null,
    DateOnly? StartsOn = null,
    TimeOnly? StartsAt = null,
    TimeSpan? Duration = null,
    bool IsDeadline = false);
