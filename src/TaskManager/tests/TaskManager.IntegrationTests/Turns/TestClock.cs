using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;

namespace TaskManager.IntegrationTests.Turns;

/// <summary>Детерминированное время для сценариев приёма запроса.</summary>
internal sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset utcNow, TimeZoneInfo? timeZone = null)
    {
        UtcNow = utcNow;
        TimeZone = timeZone ?? TimeZoneInfo.Utc;
    }

    public DateTimeOffset UtcNow { get; }

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(UtcNow, TimeZone);
}
