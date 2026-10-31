namespace TaskManager.Turns.Application.Clients.Stt;

/// <summary>
/// Ответ <c>stt</c>. Пустой текст при <see cref="Empty"/> — не транспортная ошибка, а тишина
/// в записи: ход закрывается кодом <c>EMPTY_TRANSCRIPT</c> без ретраев.
/// </summary>
public sealed record SttResult(
    Guid TurnId,
    string? Text,
    string Language,
    decimal Confidence,
    bool Empty,
    int DurationMs,
    string Provider,
    string? Model = null);
