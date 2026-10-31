using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Ports;

namespace TaskManager.Infrastructure.Clients;

/// <summary>
/// HTTP-адаптер <c>stt</c>: <c>POST /v1/transcribe</c>. Аудио не передаётся телом,
/// только presigned GET. Таймаут берётся из <see cref="SttClientOptions"/>.
/// </summary>
public sealed class SttClient : ISttClient
{
    private const string RequestPath = "v1/transcribe";
    private const string ServiceName = "stt";
    private const string ContractViolationMessage =
        "stt returned a response that does not match the contract.";

    private readonly HttpClient _httpClient;
    private readonly SttClientOptions _options;

    public SttClient(HttpClient httpClient, SttClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _options = options ?? SttClientOptions.Default;
        if (_options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "STT timeout must be positive.");
        }

        _httpClient = httpClient;
    }

    public async Task<SttResult> TranscribeAsync(
        SttRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        try
        {
            var payload = new SttRequestPayload(
                request.TurnId,
                request.AudioUrl.ToString(),
                request.ContentType,
                request.Language,
                request.MaxDurationMs);

            using var message = new HttpRequestMessage(HttpMethod.Post, RequestPath)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, ServiceJson.Options),
                    Encoding.UTF8,
                    "application/json"),
            };

            using HttpResponseMessage response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            string body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new SttException(
                    ServiceErrorResponse.Describe(ServiceName, response.StatusCode, body),
                    (int)response.StatusCode,
                    isPermanent: ServiceErrorResponse.IsPermanent(response.StatusCode, body));
            }

            SttResponsePayload result;
            try
            {
                result = JsonSerializer.Deserialize<SttResponsePayload>(
                        body,
                        ServiceJson.Options)
                    ?? throw new JsonException("stt returned an empty response.");

                EnsureContract(result, request.TurnId);
            }
            catch (JsonException error)
            {
                throw new SttException(
                    ContractViolationMessage,
                    (int)response.StatusCode,
                    error,
                    isPermanent: true);
            }

            return new SttResult(
                result.TurnId,
                result.Text,
                result.Language,
                result.Confidence,
                result.Empty,
                result.DurationMs,
                result.Provider,
                result.Model);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SttException(
                $"stt did not respond within {_options.Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds.",
                innerException: error);
        }
        catch (HttpRequestException error)
        {
            throw new SttException("stt request failed.", innerException: error);
        }
    }

    /// <summary>
    /// Ответ обязан относиться к запрошенному ходу, а <c>text</c> — согласовываться
    /// с <c>empty</c>: пустая строка только при <c>empty = true</c> (<c>stt.response.json</c>).
    /// </summary>
    private static void EnsureContract(SttResponsePayload result, Guid turnId)
    {
        if (result.TurnId != turnId)
        {
            throw new SttException(ContractViolationMessage, isPermanent: true);
        }

        if (result.Text is null || result.Empty != (result.Text.Length == 0))
        {
            throw new SttException(ContractViolationMessage, isPermanent: true);
        }
    }

    private sealed record SttRequestPayload(
        [property: JsonPropertyName("turn_id")] Guid TurnId,
        [property: JsonPropertyName("audio_url")] string AudioUrl,
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("max_duration_ms")] int? MaxDurationMs);

    private sealed record SttResponsePayload(
        [property: JsonPropertyName("turn_id")] Guid TurnId,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("confidence")] decimal Confidence,
        [property: JsonPropertyName("empty")] bool Empty,
        [property: JsonPropertyName("duration_ms")] int DurationMs,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("model")] string? Model);
}
