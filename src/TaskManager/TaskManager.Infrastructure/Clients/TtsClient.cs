using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskManager.Turns.Application.Clients.Tts;
using TaskManager.Turns.Application.Ports;

namespace TaskManager.Infrastructure.Clients;

/// <summary>
/// HTTP-адаптер <c>tts</c>: <c>POST /v1/synthesize</c>. Блоб не передаётся:
/// сервис возвращает детерминированный ключ объекта в S3. Таймаут — <c>TTS_TIMEOUT</c>.
/// </summary>
public sealed class TtsClient : ITtsClient
{
    private const string RequestPath = "v1/synthesize";
    private const string ServiceName = "tts";

    private readonly HttpClient _httpClient;
    private readonly TtsClientOptions _options;

    public TtsClient(HttpClient httpClient, TtsClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _options = options ?? TtsClientOptions.Default;
        if (_options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "TTS timeout must be positive.");
        }

        _httpClient = httpClient;
    }

    public async Task<TtsResult> SynthesizeAsync(
        TtsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        try
        {
            var payload = new TtsRequestPayload(
                request.AnswerId,
                request.Text,
                request.Voice,
                request.Format,
                request.Speed);

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
                throw new TtsException(
                    ServiceErrorResponse.Describe(ServiceName, response.StatusCode, body),
                    (int)response.StatusCode,
                    isPermanent: ServiceErrorResponse.IsPermanent(response.StatusCode, body));
            }

            TtsResponsePayload result;
            try
            {
                result = JsonSerializer.Deserialize<TtsResponsePayload>(
                        body,
                        ServiceJson.Options)
                    ?? throw new JsonException("tts returned an empty response.");
            }
            catch (JsonException error)
            {
                throw new TtsException(
                    "tts returned a response that does not match the contract.",
                    (int)response.StatusCode,
                    error,
                    isPermanent: true);
            }

            return new TtsResult(
                result.AnswerId,
                result.ObjectKey,
                result.ContentType,
                result.Format,
                result.DurationMs,
                result.Cached);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TtsException(
                $"tts did not respond within {_options.Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds.",
                innerException: error);
        }
        catch (HttpRequestException error)
        {
            throw new TtsException("tts request failed.", innerException: error);
        }
    }

    private sealed record TtsRequestPayload(
        [property: JsonPropertyName("answer_id")] Guid AnswerId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("voice")] string Voice,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("speed")] double Speed);

    private sealed record TtsResponsePayload(
        [property: JsonPropertyName("answer_id")] Guid AnswerId,
        [property: JsonPropertyName("object_key")] string ObjectKey,
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("duration_ms")] int DurationMs,
        [property: JsonPropertyName("cached")] bool Cached);
}
