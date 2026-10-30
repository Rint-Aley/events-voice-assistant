namespace TaskManager.Application.Abstractions;

/// <summary>
/// Объектное хранилище аудио. Блобы не проходят через TaskManager: только ключи
/// и presigned-ссылки.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// Создаёт ключ объекта под загруженный клиентом файл и presigned PUT.
    /// Тип содержимого уже проверен вызывающим кодом.
    /// </summary>
    Task<PresignedUpload> CreatePresignedUploadAsync(
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Создаёт presigned GET на существующий объект.</summary>
    Task<Uri> CreatePresignedDownloadUrlAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Тип содержимого объекта по расширению ключа. Нужен <c>stt</c>: контракт требует
    /// тот же <c>content_type</c>, с которым клиент загрузил файл. <c>HeadObject</c>
    /// не используется.
    /// </summary>
    string GetContentType(string objectKey);
}
