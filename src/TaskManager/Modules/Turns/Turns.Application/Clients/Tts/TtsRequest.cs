namespace TaskManager.Turns.Application.Clients.Tts;

/// <summary>
/// Запрос в <c>tts</c>. Озвучивается готовый текст ответа; <see cref="AnswerId"/> —
/// ключ идемпотентности.
/// </summary>
public sealed record TtsRequest(
    Guid AnswerId,
    string Text,
    string Voice,
    string Format,
    double Speed = 1);
