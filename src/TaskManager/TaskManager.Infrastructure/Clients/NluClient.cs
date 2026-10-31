using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Clients;

/// <summary>
/// HTTP-адаптер <c>nlu</c>: <c>POST /v1/interpret</c>. Поля запроса и ответа
/// соответствуют <c>nlu.request.json</c> и <c>nlu.response.json</c>; неизвестные
/// необязательные поля старого ответа не ломают разбор. Таймаут — <c>NLU_TIMEOUT</c>.
/// </summary>
public sealed class NluClient : INluClient
{
    private const string RequestPath = "v1/interpret";
    private const string ServiceName = "nlu";
    private const string ContractViolationMessage =
        "nlu returned a response that does not match the contract.";

    private readonly HttpClient _httpClient;
    private readonly NluClientOptions _options;

    public NluClient(HttpClient httpClient, NluClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _options = options ?? NluClientOptions.Default;
        if (_options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "NLU timeout must be positive.");
        }

        _httpClient = httpClient;
    }

    public async Task<NluResponse> InterpretAsync(
        NluRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        try
        {
            var payload = new NluRequestPayload(
                request.TurnId,
                request.Text,
                request.Now,
                request.TimeZone,
                ToPayload(request.Draft));

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
                throw new NluException(
                    ServiceErrorResponse.Describe(ServiceName, response.StatusCode, body),
                    (int)response.StatusCode,
                    isPermanent: ServiceErrorResponse.IsPermanent(response.StatusCode, body));
            }

            NluResponsePayload result;
            try
            {
                result = JsonSerializer.Deserialize<NluResponsePayload>(
                        body,
                        ServiceJson.Options)
                    ?? throw new JsonException("nlu returned an empty response.");

                EnsureContract(result, request.TurnId);
            }
            catch (JsonException error)
            {
                throw new NluException(
                    ContractViolationMessage,
                    (int)response.StatusCode,
                    error,
                    isPermanent: true);
            }

            return new NluResponse(
                result.TurnId,
                result.Action,
                result.Confidence,
                ToEntities(result.Entities),
                result.Missing,
                result.Question,
                ToEmbedding(result.Embedding));
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NluException(
                $"nlu did not respond within {_options.Timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds.",
                innerException: error);
        }
        catch (HttpRequestException error)
        {
            throw new NluException("nlu request failed.", innerException: error);
        }
    }

    private static NluDraftPayload? ToPayload(NluDraft? draft) =>
        draft is null
            ? null
            : new NluDraftPayload(
                ToContractKind(draft.Kind),
                draft.Description,
                draft.StartsOn,
                draft.StartsAt,
                draft.DurationMinutes);

    private static string? ToContractKind(DraftKind? kind) =>
        kind switch
        {
            DraftKind.Task => "task",
            DraftKind.Reminder => "reminder",
            DraftKind.Meeting => "meeting",
            _ => null,
        };

    private static NluEntities? ToEntities(NluEntitiesPayload? entities) =>
        entities is null
            ? null
            : new NluEntities(
                entities.Description,
                entities.StartsOn,
                entities.StartsAt,
                entities.DurationMinutes,
                entities.PeriodFrom,
                entities.PeriodTo,
                entities.Query,
                entities.TypeFilter,
                entities.IsDeadline);

    private static NluEmbedding? ToEmbedding(NluEmbeddingPayload? embedding) =>
        embedding is null
            ? null
            : new NluEmbedding(embedding.Model!, embedding.Dim, embedding.Values!);

    /// <summary>
    /// Ответ обязан относиться к запрошенному ходу, а объект <c>embedding</c> — содержать
    /// <c>model</c> и <c>values</c>. Битый JSON и незаполненные обязательные поля одинаково
    /// означают постоянную ошибку: повтор запроса не поможет.
    /// </summary>
    private static void EnsureContract(NluResponsePayload result, Guid turnId)
    {
        if (result.TurnId != turnId)
        {
            throw new NluException(ContractViolationMessage, isPermanent: true);
        }

        if (result.Embedding is { } embedding
            && (string.IsNullOrWhiteSpace(embedding.Model) || embedding.Values is null))
        {
            throw new NluException(ContractViolationMessage, isPermanent: true);
        }
    }

    private sealed record NluRequestPayload(
        [property: JsonPropertyName("turn_id")] Guid TurnId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("now")] DateTimeOffset Now,
        [property: JsonPropertyName("timezone")] string TimeZone,
        [property: JsonPropertyName("draft")] NluDraftPayload? Draft);

    private sealed record NluDraftPayload(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("starts_on")] string? StartsOn,
        [property: JsonPropertyName("starts_at")] string? StartsAt,
        [property: JsonPropertyName("duration_minutes")] int? DurationMinutes);

    private sealed record NluResponsePayload(
        [property: JsonPropertyName("turn_id")] Guid TurnId,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("entities")] NluEntitiesPayload? Entities,
        [property: JsonPropertyName("missing")] IReadOnlyList<string>? Missing,
        [property: JsonPropertyName("question")] string? Question,
        [property: JsonPropertyName("embedding")] NluEmbeddingPayload? Embedding);

    private sealed record NluEntitiesPayload(
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("starts_on")] string? StartsOn,
        [property: JsonPropertyName("starts_at")] string? StartsAt,
        [property: JsonPropertyName("duration_minutes")] int? DurationMinutes,
        [property: JsonPropertyName("period_from")] string? PeriodFrom,
        [property: JsonPropertyName("period_to")] string? PeriodTo,
        [property: JsonPropertyName("query")] string? Query,
        [property: JsonPropertyName("type_filter")] string? TypeFilter,
        [property: JsonPropertyName("is_deadline")] bool? IsDeadline);

    private sealed record NluEmbeddingPayload(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("dim")] int Dim,
        [property: JsonPropertyName("values")] IReadOnlyList<double>? Values);
}
