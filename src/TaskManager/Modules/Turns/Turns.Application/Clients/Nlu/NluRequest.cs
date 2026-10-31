namespace TaskManager.Turns.Application.Clients.Nlu;

/// <summary>
/// Запрос в <c>nlu</c>. Текст уже введён или распознан; черновик передаётся, если
/// предыдущий ход закончился уточнением.
/// </summary>
public sealed record NluRequest(
    Guid TurnId,
    string Text,
    DateTimeOffset Now,
    string TimeZone,
    NluDraft? Draft);
