namespace TaskManager.Turns.Application.Clients.Tts;

/// <summary>
/// Ответ <c>tts</c>: блоб не передаётся, только ключ объекта и длительность.
/// </summary>
public sealed record TtsResult(
    Guid AnswerId,
    string ObjectKey,
    string ContentType,
    string Format,
    int DurationMs,
    bool Cached);
