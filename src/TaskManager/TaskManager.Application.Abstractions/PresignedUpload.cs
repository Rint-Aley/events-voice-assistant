namespace TaskManager.Application.Abstractions;

/// <summary>
/// Presigned PUT для загрузки клиентом. Ключ объекта выбирает хранилище,
/// TTL ссылки — конфигурация инфраструктуры.
/// </summary>
/// <param name="ObjectKey">Ключ, который клиент передаёт в <c>POST /messages</c>.</param>
/// <param name="Url">Ссылка для PUT без заголовка <c>Authorization</c>.</param>
/// <param name="ExpiresAt">Момент истечения ссылки.</param>
public sealed record PresignedUpload(string ObjectKey, Uri Url, DateTimeOffset ExpiresAt);
