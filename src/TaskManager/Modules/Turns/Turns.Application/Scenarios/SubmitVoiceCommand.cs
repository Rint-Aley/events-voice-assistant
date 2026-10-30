namespace TaskManager.Turns.Application.Scenarios;

/// <summary>Голосовой запрос: клиент уже загрузил аудио и передал ключ объекта в S3.</summary>
public sealed record SubmitVoiceCommand(
    Guid SessionId,
    Guid IdempotencyKey,
    string AudioObjectKey);
