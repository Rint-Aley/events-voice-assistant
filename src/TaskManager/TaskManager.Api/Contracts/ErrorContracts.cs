using System.Text.Json.Serialization;

namespace TaskManager.Api.Contracts;

/// <summary>Тело любого не-2xx ответа TaskManager. Форма совпадает с <c>error.json</c>.</summary>
public sealed record ErrorResponse(ErrorBody Error);

/// <summary>Машинный код, человекочитаемое сообщение и признак повторяемости.</summary>
public sealed record ErrorBody(
    string Code,
    string Message,
    bool Retryable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, object?>? Details = null);
