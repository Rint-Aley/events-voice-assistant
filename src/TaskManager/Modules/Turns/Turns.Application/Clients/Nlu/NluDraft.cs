using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Clients.Nlu;

/// <summary>
/// Слоты предыдущего уточнения. Дата и время — строки, чтобы невалидные значения
/// не ломали разбор и не попадали в домен.
/// </summary>
public sealed record NluDraft(
    DraftKind? Kind,
    string? Description,
    string? StartsOn,
    string? StartsAt,
    int? DurationMinutes);
