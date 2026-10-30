namespace TaskManager.Api.Contracts;

public sealed record UploadRequest(string? ContentType, long? SizeBytes);

public sealed record UploadResponse(string ObjectKey, string UploadUrl, DateTimeOffset ExpiresAt);

public sealed record MessageRequest(
    string? SessionId,
    string? IdempotencyKey,
    string? Text,
    string? ObjectKey);

public sealed record MessageResponse(Guid TurnId, string State, string EventsUrl);

public sealed record EventResponse(
    Guid Id,
    string Kind,
    string Description,
    string? StartsOn,
    string? StartsAt,
    bool IsDeadline);

public sealed record AudioResponse(string Url, int? DurationMs);

public sealed record TurnErrorResponse(string Code, string Message);

public sealed record TurnResponse(
    Guid TurnId,
    string State,
    string? TextInput,
    string? Answer,
    IReadOnlyList<EventResponse> Events,
    int? TotalCount,
    IReadOnlyList<string> Warnings,
    AudioResponse? Audio,
    TurnErrorResponse? Error);

public sealed record DraftCancelledResponse(string Text);

public sealed record HealthResponse(string Status, string Db, string Scheduler);

public sealed record StateStreamData(string State);

public sealed record AnswerStreamData(
    string Text,
    IReadOnlyList<EventResponse> Events,
    int? TotalCount,
    IReadOnlyList<string> Warnings);

public sealed record AudioStreamData(string Url, int? DurationMs);

public sealed record ErrorStreamData(string Code, string Message);
