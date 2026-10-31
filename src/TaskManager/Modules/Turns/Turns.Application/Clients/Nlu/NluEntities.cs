namespace TaskManager.Turns.Application.Clients.Nlu;

/// <summary>
/// Сущности плана <c>nlu</c>. <c>starts_on</c> и <c>starts_at</c> принимаются строками:
/// несуществующая дата или время не должны ронять десериализацию.
/// <c>type_filter</c> и <c>is_deadline</c> необязательны: старый <c>nlu</c> их не присылает.
/// </summary>
public sealed record NluEntities(
    string? Description = null,
    string? StartsOn = null,
    string? StartsAt = null,
    int? DurationMinutes = null,
    string? PeriodFrom = null,
    string? PeriodTo = null,
    string? Query = null,
    string? TypeFilter = null,
    bool? IsDeadline = null);
