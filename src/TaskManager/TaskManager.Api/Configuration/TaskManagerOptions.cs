namespace TaskManager.Api.Configuration;

/// <summary>
/// Конфигурация HTTP-слоя. Значения читаются из переменных окружения, описанных в
/// <c>docs/task-manager/implementation.md</c> §12. <c>SSE_POLL_INTERVAL_MS</c> — внутренняя
/// настройка транспорта: как часто Api сверяет снапшот при живом потоке.
/// </summary>
public sealed record TaskManagerOptions
{
    private static readonly Guid DefaultSessionId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    public required Guid SessionId { get; init; }

    public required string UserId { get; init; }

    public required TimeZoneInfo TimeZone { get; init; }

    public required TimeSpan Deadline { get; init; }

    public required TimeSpan SseAudioWait { get; init; }

    public required TimeSpan SsePingInterval { get; init; }

    public required TimeSpan SsePollInterval { get; init; }

    public static TaskManagerOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string timeZoneId = configuration["TIMEZONE"] ?? "Europe/Moscow";

        return new TaskManagerOptions
        {
            SessionId = ParseGuid(configuration["SESSION_ID"], DefaultSessionId),
            UserId = configuration["USER_ID"] ?? "demo-user",
            TimeZone = ResolveTimeZone(timeZoneId),
            Deadline = TimeSpan.FromSeconds(ReadInt(configuration, "DEADLINE_SECONDS", 90)),
            SseAudioWait = TimeSpan.FromSeconds(
                ReadInt(configuration, "SSE_AUDIO_WAIT_SECONDS", 30)),
            SsePingInterval = TimeSpan.FromSeconds(
                ReadInt(configuration, "SSE_PING_SECONDS", 15)),
            SsePollInterval = TimeSpan.FromMilliseconds(
                ReadInt(configuration, "SSE_POLL_INTERVAL_MS", 100)),
        };
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback)
    {
        int? value = configuration.GetValue<int?>(key);

        return value ?? fallback;
    }

    private static Guid ParseGuid(string? value, Guid fallback) =>
        Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty ? parsed : fallback;

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException error)
        {
            throw new InvalidOperationException(
                $"TIMEZONE '{id}' is not available on this host.",
                error);
        }
        catch (InvalidTimeZoneException error)
        {
            throw new InvalidOperationException(
                $"TIMEZONE '{id}' is invalid.",
                error);
        }
    }
}
