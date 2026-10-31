using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskManager.Api.Contracts;

/// <summary>
/// Единые настройки JSON для HTTP-контракта web ↔ TaskManager: имена полей в
/// <c>snake_case</c>, необязательные поля не теряются при чтении старого клиента.
/// </summary>
public static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        return options;
    }
}
