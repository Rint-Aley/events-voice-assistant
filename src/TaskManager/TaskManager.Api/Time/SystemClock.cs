using TaskManager.SharedKernel;

namespace TaskManager.Api.Time;

/// <summary>
/// Часы приложения: UTC для хранения и <c>TIMEZONE</c> для относительных дат.
/// </summary>
public sealed class SystemClock(TimeZoneInfo timeZone) : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeZoneInfo TimeZone { get; } = timeZone
        ?? throw new ArgumentNullException(nameof(timeZone));

    public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(UtcNow, TimeZone);
}
