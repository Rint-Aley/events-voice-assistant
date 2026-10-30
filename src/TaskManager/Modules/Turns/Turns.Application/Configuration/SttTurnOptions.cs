namespace TaskManager.Turns.Application.Configuration;

/// <summary>
/// Параметры запроса в <c>stt</c>. Язык задаётся конфигурацией; <c>content_type</c>
/// берётся из ключа загруженного объекта, потому что контракт требует тип, с которым
/// клиент загрузил файл. Аудио в TaskManager не передаётся, только presigned GET.
/// </summary>
public sealed record SttTurnOptions(
    string Language,
    int? MaxDurationMs = null)
{
    public static SttTurnOptions Default { get; } = new("ru");
}
