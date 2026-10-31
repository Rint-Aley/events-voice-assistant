using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskManager.Infrastructure.Clients;

/// <summary>
/// Тело ошибки по <c>error.json</c>. Форма одна для всех сервисов; сообщение уходит
/// в исключение только для логов и не показывается пользователю как есть.
/// </summary>
internal sealed record ServiceErrorEnvelope(
    [property: JsonPropertyName("error")] ServiceErrorDetail? Error);

internal sealed record ServiceErrorDetail(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("retryable")] bool? Retryable = null);

internal static class ServiceErrorResponse
{
    public static string Describe(string service, HttpStatusCode status, string body)
    {
        if (TryRead(body) is { Code: not null } error)
        {
            string message = string.IsNullOrWhiteSpace(error.Message)
                ? error.Code
                : $"{error.Code}: {error.Message}";

            return $"{service} returned HTTP {(int)status}: {message}";
        }

        return $"{service} returned HTTP {(int)status} ({status})";
    }

    /// <summary>
    /// 4xx — постоянная ошибка. Для 5xx решение уточняется флагом <c>retryable</c>:
    /// <c>false</c> отменяет повтор, даже когда код ответа допускает повтор
    /// (<c>docs/contracts/README.md</c>). Отсутствующее тело флаг не заменяет.
    /// </summary>
    public static bool IsPermanent(HttpStatusCode status, string? body)
    {
        if ((int)status is >= 400 and < 500)
        {
            return true;
        }

        return TryRead(body)?.Retryable is false;
    }

    private static ServiceErrorDetail? TryRead(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ServiceErrorEnvelope>(body, ServiceJson.Options)?.Error;
        }
        catch (JsonException)
        {
            // Тело ошибки не по контракту: для диагностики достаточно HTTP-кода.
            return null;
        }
    }
}
