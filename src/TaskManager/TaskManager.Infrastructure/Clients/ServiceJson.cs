using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskManager.Infrastructure.Clients;

/// <summary>Единые настройки JSON для вызовов <c>stt</c>, <c>nlu</c> и <c>tts</c>.</summary>
internal static class ServiceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
