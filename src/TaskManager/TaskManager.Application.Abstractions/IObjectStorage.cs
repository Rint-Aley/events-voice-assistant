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
}
