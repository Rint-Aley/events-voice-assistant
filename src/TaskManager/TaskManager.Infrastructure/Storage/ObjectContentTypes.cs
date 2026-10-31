using System.Collections.Frozen;

namespace TaskManager.Infrastructure.Storage;

/// <summary>
/// Тип содержимого объекта по расширению <c>object_key</c>. <c>HeadObject</c> не
/// используется; неизвестное или отсутствующее расширение — <c>application/octet-stream</c>.
/// </summary>
public static class ObjectContentTypes
{
    public const string Default = "application/octet-stream";

    private static readonly FrozenDictionary<string, string> ByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".webm"] = "audio/webm",
            [".ogg"] = "audio/ogg",
            [".opus"] = "audio/ogg",
            [".wav"] = "audio/wav",
            [".mp3"] = "audio/mpeg",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ByContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["audio/webm"] = ".webm",
            ["audio/ogg"] = ".ogg",
            ["audio/wav"] = ".wav",
            ["audio/mpeg"] = ".mp3",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Определяет <c>content_type</c> по расширению ключа объекта.</summary>
    public static string FromObjectKey(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        string extension = Path.GetExtension(objectKey);

        return extension.Length > 0 && ByExtension.TryGetValue(extension, out string? contentType)
            ? contentType
            : Default;
    }

    /// <summary>
    /// Расширение для ключа загружаемого объекта. Неподдерживаемый тип получает
    /// <c>.bin</c>; вызывающий код проверяет допустимые типы до вызова хранилища.
    /// </summary>
    public static string ExtensionForContentType(string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        return ByContentType.TryGetValue(contentType, out string? extension)
            ? extension
            : ".bin";
    }
}
