namespace TaskManager.Events.Domain.Tests;

public sealed class ConflictDetectorTests
{
    private static readonly DateOnly Date = new(2026, 10, 30);

    [Fact]
    public void Detect_ReturnsAllOverlappingMeetings()
    {
        var candidate = Meeting("Кандидат", 10, 0, 120);
        var before = Meeting("Перед", 9, 0, 60);
        var first = Meeting("Первая", 10, 30, 30);
        var second = Meeting("Вторая", 11, 0, 30);
        var after = Meeting("После", 12, 0, 60);

        var conflicts = ConflictDetector.Detect(
            candidate,
            new[] { before, first, second, after });

        Assert.Equal(
            new[] { first.Id, second.Id },
            conflicts.Select(@event => @event.Id));
    }

    [Theory]
    [InlineData(9, 0, 60, false)]
    [InlineData(10, 0, 60, true)]
    [InlineData(10, 30, 60, true)]
    [InlineData(11, 0, 60, false)]
    [InlineData(9, 30, 120, true)]
    [InlineData(10, 30, 10, true)]
    public void Detect_UsesHalfOpenInterval(int hour, int minute, int durationMinutes, bool expected)
    {
        var candidate = Meeting("Кандидат", 10, 0, 60);
        var existing = Meeting("Существующая", hour, minute, durationMinutes);

        var conflicts = ConflictDetector.Detect(candidate, new[] { existing });

        Assert.Equal(expected, conflicts.Count == 1);
    }

    [Theory]
    [InlineData(EventStatus.Done)]
    [InlineData(EventStatus.Cancelled)]
    public void Detect_IgnoresNonActiveMeetings(EventStatus status)
    {
        var candidate = Meeting("Кандидат", 10, 0, 60);
        var existing = Meeting("Существующая", 10, 30, 30, status);

        var conflicts = ConflictDetector.Detect(candidate, new[] { existing });

        Assert.Empty(conflicts);
    }

    [Theory]
    [InlineData(EventKind.Task)]
    [InlineData(EventKind.Reminder)]
    public void Detect_IgnoresNonMeetingEvents(EventKind kind)
    {
        var candidate = Meeting("Кандидат", 10, 0, 60);
        var existing = Other(kind, "Другая запись", 10, 30);

        var conflicts = ConflictDetector.Detect(candidate, new[] { existing });

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Detect_ReturnsNothingForNonMeetingCandidate()
    {
        var candidate = Other(EventKind.Task, "Задача", 10, 0);
        var existing = Meeting("Встреча", 10, 0, 60);

        var conflicts = ConflictDetector.Detect(candidate, new[] { existing });

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Detect_IgnoresMeetingsOnOtherDates()
    {
        var candidate = Meeting("Кандидат", 10, 0, 60);
        var other = Meeting("Другая", 10, 0, 60, date: Date.AddDays(1));

        var conflicts = ConflictDetector.Detect(candidate, new[] { other });

        Assert.Empty(conflicts);
    }

    private static Event Meeting(
        string description,
        int hour,
        int minute,
        int durationMinutes,
        EventStatus status = EventStatus.Active,
        DateOnly? date = null)
    {
        var result = Event.Restore(
            Guid.NewGuid(),
            EventKind.Meeting,
            status,
            description,
            startsOn: date ?? Date,
            startsAt: new TimeOnly(hour, minute),
            duration: TimeSpan.FromMinutes(durationMinutes));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static Event Other(EventKind kind, string description, int hour, int minute)
    {
        var result = Event.Restore(
            Guid.NewGuid(),
            kind,
            EventStatus.Active,
            description,
            startsOn: Date,
            startsAt: new TimeOnly(hour, minute));

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
