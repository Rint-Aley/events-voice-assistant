namespace TaskManager.Turns.Application.Clients.Stt;

/// <summary>
/// Запрос в <c>stt</c>. Аудио не передаётся телом: только presigned GET на объект в S3.
/// </summary>
public sealed record SttRequest(
    Guid TurnId,
    Uri AudioUrl,
    string ContentType,
    string Language,
    int? MaxDurationMs = null);
