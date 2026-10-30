namespace TaskManager.Events.Domain.Tests;

public sealed class EventSelectionTests
{
    private static readonly DateOnly Day = new(2026, 10, 30);

    [Fact]
    public void Filter_WithoutTypeFilter_ReturnsAllKinds()
    {
        var task = CreateEvent(EventKind.Task, "Задача", Day);
        var reminder = CreateEvent(EventKind.Reminder, "Напоминание", Day, new TimeOnly(9, 0));
        var meeting = CreateEvent(EventKind.Meeting, "Встреча", Day, new TimeOnly(10, 0));

        var result = EventSelection.Filter([task, reminder, meeting]);

        Assert.Equal(
            [task.Id, reminder.Id, meeting.Id],
            result.Select(@event => @event.Id));
    }

    [Fact]
    public void Filter_Meeting_ReturnsOnlyMeetings()
    {
        var task = CreateEvent(EventKind.Task, "Задача", Day);
        var reminder = CreateEvent(EventKind.Reminder, "Напоминание", Day, new TimeOnly(9, 0));
        var meeting = CreateEvent(EventKind.Meeting, "Встреча", Day, new TimeOnly(10, 0));

        var result = EventSelection.Filter(
            [task, reminder, meeting],
            EventTypeFilter.Meeting);

        Assert.Equal([meeting.Id], result.Select(@event => @event.Id));
    }

    [Fact]
    public void Filter_Task_ReturnsTasksAndReminders()
    {
        var task = CreateEvent(EventKind.Task, "Задача", Day);
        var reminder = CreateEvent(EventKind.Reminder, "Напоминание", Day, new TimeOnly(9, 0));
        var meeting = CreateEvent(EventKind.Meeting, "Встреча", Day, new TimeOnly(10, 0));

        var result = EventSelection.Filter(
            [task, reminder, meeting],
            EventTypeFilter.Task);

        Assert.Equal(
            [task.Id, reminder.Id],
            result.Select(@event => @event.Id));
    }

    [Fact]
    public void Filter_Reminder_ReturnsOnlyReminders()
    {
        var task = CreateEvent(EventKind.Task, "Задача", Day);
        var reminder = CreateEvent(EventKind.Reminder, "Напоминание", Day, new TimeOnly(9, 0));
        var meeting = CreateEvent(EventKind.Meeting, "Встреча", Day, new TimeOnly(10, 0));

        var result = EventSelection.Filter(
            [task, reminder, meeting],
            EventTypeFilter.Reminder);

        Assert.Equal([reminder.Id], result.Select(@event => @event.Id));
    }

    [Fact]
    public void Filter_ExcludesCancelled_AndKeepsActiveAndDone()
    {
        var active = CreateEvent(EventKind.Task, "Активная", Day, status: EventStatus.Active);
        var done = CreateEvent(EventKind.Task, "Выполненная", Day, status: EventStatus.Done);
        var cancelled = CreateEvent(EventKind.Task, "Отменённая", Day, status: EventStatus.Cancelled);

        var result = EventSelection.Filter([active, done, cancelled]);

        Assert.Equal(
            new[] { active.Id, done.Id },
            result.Select(@event => @event.Id));
    }

    [Fact]
    public void ListSections_OrdersByDateThenTime_AndPutsUndatedLast()
    {
        var laterDay = CreateEvent(EventKind.Meeting, "Позже", Day.AddDays(1), new TimeOnly(9, 0));
        var sameDayWithoutTime = CreateEvent(EventKind.Task, "Без времени", Day);
        var sameDayMorning = CreateEvent(EventKind.Reminder, "Утром", Day, new TimeOnly(8, 0));
        var sameDayEvening = CreateEvent(EventKind.Meeting, "Вечером", Day, new TimeOnly(18, 0));
        var undated = CreateEvent(EventKind.Task, "Без даты");

        var sections = EventSelection.ListSections(
            [laterDay, undated, sameDayWithoutTime, sameDayEvening, sameDayMorning]);

        Assert.Equal(
            [
                sameDayMorning.Id,
                sameDayEvening.Id,
                sameDayWithoutTime.Id,
                laterDay.Id,
            ],
            sections.Dated.Select(@event => @event.Id));
        Assert.Equal([undated.Id], sections.Undated.Select(@event => @event.Id));
    }

    [Fact]
    public void ListSections_ExcludesCancelledEvents()
    {
        var active = CreateEvent(EventKind.Task, "Активная", Day);
        var cancelled = CreateEvent(EventKind.Task, "Отменённая", Day, status: EventStatus.Cancelled);

        var sections = EventSelection.ListSections([active, cancelled]);

        Assert.Single(sections.Dated);
        Assert.Equal(active.Id, sections.Dated[0].Id);
        Assert.Empty(sections.Undated);
    }

    [Fact]
    public void ListSections_AppliesTypeFilter()
    {
        var undatedTask = CreateEvent(EventKind.Task, "Задача без даты");
        var reminder = CreateEvent(EventKind.Reminder, "Напоминание", Day, new TimeOnly(9, 0));
        var meeting = CreateEvent(EventKind.Meeting, "Встреча", Day, new TimeOnly(10, 0));

        var sections = EventSelection.ListSections(
            [undatedTask, reminder, meeting],
            EventTypeFilter.Task
        );

        Assert.Equal([reminder.Id], sections.Dated.Select(@event => @event.Id));
        Assert.Equal([undatedTask.Id], sections.Undated.Select(@event => @event.Id));
    }

    private static Event CreateEvent(
        EventKind kind,
        string description,
        DateOnly? startsOn = null,
        TimeOnly? startsAt = null,
        EventStatus status = EventStatus.Active)
    {
        var result = Event.Restore(
            Guid.NewGuid(),
            kind,
            status,
            description,
            startsOn: startsOn,
            startsAt: startsAt);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
