using System.Text.Json;
using System.Text.Json.Serialization;
using TaskManager.Turns.Application.Clients.Nlu;

namespace TaskManager.Turns.Application.Plans;

/// <summary>
/// Нормализованная полезная нагрузка команды <c>event.create</c>. Поля повторяют
/// <c>domain_commands.payload</c> из <c>task-manager.md</c> §7.3.
/// </summary>
public sealed record EventCreatePayload(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("source_text")] string? SourceText,
    [property: JsonPropertyName("starts_on")] DateOnly? StartsOn,
    [property: JsonPropertyName("starts_at")] TimeOnly? StartsAt,
    [property: JsonPropertyName("duration")] TimeSpan? Duration,
    [property: JsonPropertyName("is_deadline")] bool IsDeadline,
    [property: JsonPropertyName("embedding")] NluEmbedding? Embedding = null);

/// <summary>Сериализация внутреннего payload команды.</summary>
public static class EventCreatePayloadJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(EventCreatePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return JsonSerializer.Serialize(payload, Options);
    }

    public static bool TryDeserialize(string? json, out EventCreatePayload? payload)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<EventCreatePayload>(json, Options);
            return payload is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
