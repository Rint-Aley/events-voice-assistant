using FluentAssertions;

namespace TaskManager.Events.Domain.Tests;

public sealed class EventCreateTests
{
    private static readonly DateOnly Date = new(2026, 10, 30);
    private static readonly TimeOnly Time = new(10, 0);

    [Theory]
    [InlineData(EventKind.Task)]
    [InlineData(EventKind.Reminder)]
    [InlineData(EventKind.Meeting)]
    public void Create_RequiresDescription(EventKind kind)
    {
        var result = Event.Create(kind, "   ", startsOn: Date, startsAt: Time);

        Assert.True(result.IsFailure);
        Assert.Equal(EventErrors.DescriptionRequired.Code, result.Error!.Code);
    }

    [Fact]
    public void Create_TaskWithoutDateAndTime_IsActiveAndHasNoSchedule()
    {
        string taskDescription = "Позвонить маме";
        var result = Event.Create(EventKind.Task, $"  {taskDescription}  ");

        Assert.True(result.IsSuccess);
        var task = result.Value;
        Assert.Equal(EventKind.Task, task.Kind);
        Assert.Equal(EventStatus.Active, task.Status);
        Assert.Equal(taskDescription, task.Description);
        Assert.Null(task.StartsOn);
        Assert.Null(task.StartsAt);
        Assert.Null(task.Duration);
        Assert.False(task.IsDeadline);
    }

    [Fact]
    public void Create_TaskWithDateOnly_IsAllowed()
    {
        var result = Event.Create(EventKind.Task, "Сдать отчёт", startsOn: Date);

        Assert.True(result.IsSuccess);
        Assert.Equal(Date, result.Value.StartsOn);
        Assert.Null(result.Value.StartsAt);
    }

    [Fact]
    public void Create_TaskWithTimeWithoutDate_Fails()
    {
        var result = Event.Create(EventKind.Task, "Позвонить маме", startsAt: Time);

        Assert.True(result.IsFailure);
        Assert.Equal(EventErrors.TimeWithoutDate.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(EventKind.Reminder)]
    [InlineData(EventKind.Meeting)]
    public void Create_ReminderAndMeetingRequireDate(EventKind kind)
    {
        var result = Event.Create(kind, "Созвон", startsAt: Time);

        Assert.True(result.IsFailure);
        Assert.Equal(EventErrors.DateRequired.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(EventKind.Reminder)]
    [InlineData(EventKind.Meeting)]
    public void Create_ReminderAndMeetingRequireTime(EventKind kind)
    {
        var result = Event.Create(kind, "Созвон", startsOn: Date);

        Assert.True(result.IsFailure);
        Assert.Equal(EventErrors.TimeRequired.Code, result.Error!.Code);
    }

    [Fact]
    public void Create_MeetingWithoutDuration_DefaultsToSixtyMinutes()
    {
        var result = Event.Create(EventKind.Meeting, "Обсудить релиз", startsOn: Date, startsAt: Time);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromMinutes(60), result.Value.Duration);
    }

    [Fact]
    public void Create_MeetingKeepsProvidedDuration()
    {
        var result = Event.Create(
            EventKind.Meeting,
            "Обсудить релиз",
            startsOn: Date,
            startsAt: Time,
            duration: TimeSpan.FromMinutes(30));

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Value.Duration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_MeetingRejectsNonPositiveDuration(int minutes)
    {
        var result = Event.Create(
            EventKind.Meeting,
            "Обсудить релиз",
            startsOn: Date,
            startsAt: Time,
            duration: TimeSpan.FromMinutes(minutes));

        Assert.True(result.IsFailure);
        Assert.Equal(EventErrors.DurationNotPositive.Code, result.Error!.Code);
    }

    [Theory]
    [InlineData(EventKind.Task)]
    [InlineData(EventKind.Reminder)]
    public void Create_NonMeetingRejectsDuration(EventKind kind)
    {
        var result = Event.Create(
            kind,
            "Позвонить маме",
            startsOn: Date,
            startsAt: Time,
            duration: TimeSpan.FromMinutes(30));

        Assert.True(result.IsFailure);
        Assert.Equal(EventErrors.DurationOnlyForMeeting.Code, result.Error!.Code);
    }

    [Fact]
    public void Create_TaskAllowsIsDeadline()
    {
        var result = Event.Create(EventKind.Task, "Сдать отчёт", startsOn: Date, isDeadline: true);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsDeadline);
    }

    [Theory]
    [InlineData(EventKind.Reminder)]
    [InlineData(EventKind.Meeting)]
    public void Create_IsDeadlineOnlyForTask(EventKind kind)
    {
        var result = Event.Create(
            kind,
            "Созвон",
            startsOn: Date,
            startsAt: Time,
            isDeadline: true);

        Assert.True(result.IsFailure);
        Assert.Equal(EventErrors.IsDeadlineOnlyForTask.Code, result.Error!.Code);
    }

    [Fact]
    public void Create_BlankSourceText_IsStoredAsNull()
    {
        var result = Event.Create(EventKind.Task, "Позвонить маме", sourceText: "   ");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.SourceText);
    }

    [Fact]
    public void Restore_KeepsExistingStatus()
    {
        var result = Event.Restore(
            Guid.NewGuid(),
            EventKind.Task,
            EventStatus.Cancelled,
            "Позвонить маме");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(EventStatus.Cancelled);
    }
}
