using Microsoft.EntityFrameworkCore;
using TaskManager.Events.Application.Commands;
using TaskManager.Events.Application.Queries;
using TaskManager.Events.Application.Scenarios;
using TaskManager.Events.Domain;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Infrastructure.Persistence.Queries;
using TaskManager.Infrastructure.Persistence.Repositories;
using TaskManager.SharedKernel;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Events;

public sealed class EventsApplicationTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 11, 2);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task CreateEvent_persists_task_reminder_and_meeting_with_expected_slots()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var unitOfWork = new UnitOfWork(dbContext);
        var create = CreateHandler(dbContext, unitOfWork);

        // Act
        var task = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Task,
            "  Купить хлеб  ",
            IsDeadline: true));
        var reminder = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Reminder,
            "Позвонить Ивану",
            StartsOn: Day,
            StartsAt: new TimeOnly(9, 30)));
        var meeting = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Созвон с командой",
            StartsOn: Day,
            StartsAt: new TimeOnly(11, 0)));

        // Assert
        task.IsSuccess.Should().BeTrue();
        task.Value.Event.Description.Should().Be("Купить хлеб");
        task.Value.Event.Status.Should().Be(EventStatus.Active);
        task.Value.Event.StartsOn.Should().BeNull();
        task.Value.Event.StartsAt.Should().BeNull();
        task.Value.Event.Duration.Should().BeNull();
        task.Value.Event.IsDeadline.Should().BeTrue();
        task.Value.Conflicts.Should().BeEmpty();

        reminder.IsSuccess.Should().BeTrue();
        reminder.Value.Event.StartsOn.Should().Be(Day);
        reminder.Value.Event.StartsAt.Should().Be(new TimeOnly(9, 30));
        reminder.Value.Event.Duration.Should().BeNull();

        meeting.IsSuccess.Should().BeTrue();
        meeting.Value.Event.StartsOn.Should().Be(Day);
        meeting.Value.Event.StartsAt.Should().Be(new TimeOnly(11, 0));
        meeting.Value.Event.Duration.Should().Be(TimeSpan.FromMinutes(60));

        var persisted = await dbContext.Events.AsNoTracking().ToListAsync();
        persisted.Should().HaveCount(3);
        persisted.Should().ContainSingle(@event => @event.Id == task.Value.Event.Id);
        persisted.Should().ContainSingle(@event => @event.Id == reminder.Value.Event.Id);
        persisted.Should().ContainSingle(@event => @event.Id == meeting.Value.Event.Id);
    }

    [Fact]
    public async Task CreateEvent_rejects_invalid_title_slots_type_and_fields()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var unitOfWork = new UnitOfWork(dbContext);
        var create = CreateHandler(dbContext, unitOfWork);

        // Act
        var noTitle = await CreateAsync(create, unitOfWork, new CreateEventCommand(EventKind.Task, "   "));
        var reminderWithoutDate = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Reminder,
            "Позвонить"));
        var reminderWithoutTime = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Reminder,
            "Позвонить",
            StartsOn: Day));
        var meetingWithoutTime = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Созвон",
            StartsOn: Day));
        var deadlineReminder = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Reminder,
            "Позвонить",
            StartsOn: Day,
            StartsAt: new TimeOnly(9, 0),
            IsDeadline: true));
        var taskWithDuration = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Task,
            "Купить хлеб",
            Duration: TimeSpan.FromMinutes(30)));
        var unknownKind = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            (EventKind)42,
            "Неизвестно"));
        var zeroDuration = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Созвон",
            StartsOn: Day,
            StartsAt: new TimeOnly(9, 0),
            Duration: TimeSpan.Zero));

        // Assert
        noTitle.Error!.Code.Should().Be("DESCRIPTION_REQUIRED");
        reminderWithoutDate.Error!.Code.Should().Be("DATE_REQUIRED");
        reminderWithoutTime.Error!.Code.Should().Be("TIME_REQUIRED");
        meetingWithoutTime.Error!.Code.Should().Be("TIME_REQUIRED");
        deadlineReminder.Error!.Code.Should().Be("IS_DEADLINE_ONLY_FOR_TASK");
        taskWithDuration.Error!.Code.Should().Be("DURATION_ONLY_FOR_MEETING");
        unknownKind.Error!.Code.Should().Be("KIND_INVALID");
        zeroDuration.Error!.Code.Should().Be("DURATION_NOT_POSITIVE");

        (await dbContext.Events.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateEvent_is_rolled_back_with_caller_transaction()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var unitOfWork = new UnitOfWork(dbContext);
        var create = CreateHandler(dbContext, unitOfWork);

        // Act
        await unitOfWork.BeginTransactionAsync();
        var created = await create.HandleAsync(new CreateEventCommand(
            EventKind.Task,
            "Купить хлеб"));
        await unitOfWork.RollbackAsync();

        // Assert
        created.IsSuccess.Should().BeTrue();
        (await dbContext.Events.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ListPeriod_returns_three_events_and_total_count_without_cancelled()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddDatedTask(dbContext, "Первая", Day, new TimeOnly(9, 0));
        AddDatedTask(dbContext, "Вторая", Day, new TimeOnly(10, 0));
        AddDatedTask(dbContext, "Третья", Day, new TimeOnly(11, 0));
        AddDatedTask(dbContext, "Четвёртая", Day, new TimeOnly(12, 0));
        AddDatedTask(dbContext, "Пятая", Day, new TimeOnly(13, 0));
        AddEvent(dbContext, Event.Restore(
            Guid.NewGuid(),
            EventKind.Task,
            EventStatus.Cancelled,
            "Отменённая",
            startsOn: Day,
            startsAt: new TimeOnly(14, 0)).Value);
        await dbContext.SaveChangesAsync();

        var listPeriod = new ListPeriodHandler(new EventQuery(dbContext));

        // Act
        var result = await listPeriod.HandleAsync(new ListPeriodQuery(Day, Day));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(5);
        result.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Первая", "Вторая", "Третья");
        result.Value.Undated.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPeriod_keeps_limit_of_three_including_undated_block()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddDatedTask(dbContext, "Первая", Day, new TimeOnly(9, 0));
        AddDatedTask(dbContext, "Вторая", Day, new TimeOnly(10, 0));
        AddDatedTask(dbContext, "Третья", Day, new TimeOnly(11, 0));
        AddDatedTask(dbContext, "Четвёртая", Day, new TimeOnly(12, 0));
        AddEvent(dbContext, Event.Create(EventKind.Task, "Без даты").Value);
        await dbContext.SaveChangesAsync();

        var listPeriod = new ListPeriodHandler(new EventQuery(dbContext));

        // Act
        var result = await listPeriod.HandleAsync(new ListPeriodQuery(Day, Day));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(5);
        result.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Первая", "Вторая", "Третья");
        result.Value.Dated.Should().HaveCount(3);
        result.Value.Undated.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPeriod_puts_undated_tasks_after_dated_in_separate_block()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddEvent(dbContext, Event.Create(
            EventKind.Task,
            "Без даты",
            id: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")).Value);
        AddDatedTask(dbContext, "Днём", Day, new TimeOnly(12, 0));
        AddEvent(dbContext, Event.Create(
            EventKind.Task,
            "Без времени",
            startsOn: Day).Value);
        await dbContext.SaveChangesAsync();

        var listPeriod = new ListPeriodHandler(new EventQuery(dbContext));

        // Act
        var result = await listPeriod.HandleAsync(new ListPeriodQuery(Day, Day));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Днём", "Без времени", "Без даты");
        result.Value.Dated.Select(@event => @event.Description)
            .Should().Equal("Днём", "Без времени");
        result.Value.Undated.Select(@event => @event.Description)
            .Should().Equal("Без даты");
    }

    [Fact]
    public async Task ListPeriod_rejects_reversed_period()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddEvent(dbContext, Event.Create(EventKind.Task, "Без даты").Value);
        await dbContext.SaveChangesAsync();

        var listPeriod = new ListPeriodHandler(new EventQuery(dbContext));

        // Act
        var result = await listPeriod.HandleAsync(
            new ListPeriodQuery(Day, Day.AddDays(-1)));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_PERIOD");
    }

    [Fact]
    public async Task ListPeriod_applies_type_filter()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddDatedTask(dbContext, "Задача", Day, new TimeOnly(9, 0));
        AddEvent(dbContext, Event.Create(
            EventKind.Reminder,
            "Напоминание",
            startsOn: Day,
            startsAt: new TimeOnly(10, 0)).Value);
        AddEvent(dbContext, Event.Create(
            EventKind.Meeting,
            "Встреча",
            startsOn: Day,
            startsAt: new TimeOnly(11, 0)).Value);
        await dbContext.SaveChangesAsync();

        var listPeriod = new ListPeriodHandler(new EventQuery(dbContext));

        // Act
        var meetings = await listPeriod.HandleAsync(
            new ListPeriodQuery(Day, Day, EventTypeFilter.Meeting));
        var tasks = await listPeriod.HandleAsync(
            new ListPeriodQuery(Day, Day, EventTypeFilter.Task));
        var reminders = await listPeriod.HandleAsync(
            new ListPeriodQuery(Day, Day, EventTypeFilter.Reminder));

        // Assert
        meetings.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Встреча");
        tasks.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Задача", "Напоминание");
        reminders.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Напоминание");
    }

    [Fact]
    public async Task CreateEvent_returns_overlapping_meetings_as_conflicts_and_keeps_new_meeting()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var unitOfWork = new UnitOfWork(dbContext);
        var create = CreateHandler(dbContext, unitOfWork);

        var first = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Первая встреча",
            StartsOn: Day,
            StartsAt: new TimeOnly(10, 0)));

        // Act
        var adjacent = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Соседняя встреча",
            StartsOn: Day,
            StartsAt: new TimeOnly(11, 0)));
        var overlapping = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Пересекающаяся встреча",
            StartsOn: Day,
            StartsAt: new TimeOnly(9, 30)));

        // Assert
        first.IsSuccess.Should().BeTrue();
        first.Value.Conflicts.Should().BeEmpty();

        adjacent.IsSuccess.Should().BeTrue();
        adjacent.Value.Conflicts.Should().BeEmpty();

        overlapping.IsSuccess.Should().BeTrue();
        overlapping.Value.Conflicts.Should().ContainSingle()
            .Which.Id.Should().Be(first.Value.Event.Id);

        var persisted = await dbContext.Events.AsNoTracking().ToListAsync();
        persisted.Should().HaveCount(3);
        persisted.Should().ContainSingle(@event => @event.Id == overlapping.Value.Event.Id);
    }

    [Fact]
    public async Task CreateEvent_detects_meeting_that_started_before_candidate()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var unitOfWork = new UnitOfWork(dbContext);
        var create = CreateHandler(dbContext, unitOfWork);

        var longMeeting = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Долгая встреча",
            StartsOn: Day,
            StartsAt: new TimeOnly(9, 0),
            Duration: TimeSpan.FromMinutes(180)));

        // Act
        var overlapping = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Встреча внутри долгой",
            StartsOn: Day,
            StartsAt: new TimeOnly(10, 30)));

        // Assert
        longMeeting.IsSuccess.Should().BeTrue();
        overlapping.IsSuccess.Should().BeTrue();
        overlapping.Value.Conflicts.Should().ContainSingle()
            .Which.Id.Should().Be(longMeeting.Value.Event.Id);
    }

    [Fact]
    public async Task CreateEvent_returns_all_overlapping_meetings_as_warnings()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var unitOfWork = new UnitOfWork(dbContext);
        var create = CreateHandler(dbContext, unitOfWork);

        var before = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "До",
            StartsOn: Day,
            StartsAt: new TimeOnly(9, 30)));
        var after = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "После",
            StartsOn: Day,
            StartsAt: new TimeOnly(10, 30)));

        // Act
        var overlapping = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Между",
            StartsOn: Day,
            StartsAt: new TimeOnly(10, 0)));

        // Assert
        overlapping.IsSuccess.Should().BeTrue();
        overlapping.Value.Conflicts.Select(@event => @event.Id)
            .Should().Equal(before.Value.Event.Id, after.Value.Event.Id);
    }

    [Fact]
    public async Task CreateEvent_does_not_warn_about_task_in_the_same_slot()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var unitOfWork = new UnitOfWork(dbContext);
        var create = CreateHandler(dbContext, unitOfWork);

        var task = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Task,
            "Задача",
            StartsOn: Day,
            StartsAt: new TimeOnly(10, 0)));

        // Act
        var meeting = await CreateAsync(create, unitOfWork, new CreateEventCommand(
            EventKind.Meeting,
            "Встреча",
            StartsOn: Day,
            StartsAt: new TimeOnly(10, 0)));

        // Assert
        task.IsSuccess.Should().BeTrue();
        meeting.IsSuccess.Should().BeTrue();
        meeting.Value.Conflicts.Should().BeEmpty();
    }

    private async Task<TaskManagerDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<TaskManagerDbContext>()
            .UseTaskManagerPostgres(_postgres.GetConnectionString())
            .Options;

        var dbContext = new TaskManagerDbContext(options);
        await dbContext.Database.MigrateAsync();

        return dbContext;
    }

    private static CreateEventHandler CreateHandler(
        TaskManagerDbContext dbContext,
        UnitOfWork unitOfWork)
    {
        var query = new EventQuery(dbContext);

        return new CreateEventHandler(
            new EventRepository(dbContext),
            new DetectConflictsHandler(query),
            unitOfWork);
    }

    private static async Task<Result<CreateEventResult>> CreateAsync(
        CreateEventHandler handler,
        UnitOfWork unitOfWork,
        CreateEventCommand command)
    {
        await unitOfWork.BeginTransactionAsync();

        try
        {
            Result<CreateEventResult> result = await handler.HandleAsync(command);
            await unitOfWork.CommitAsync();

            return result;
        }
        catch
        {
            await unitOfWork.RollbackAsync();
            throw;
        }
    }

    private static void AddDatedTask(
        TaskManagerDbContext dbContext,
        string description,
        DateOnly startsOn,
        TimeOnly startsAt)
    {
        AddEvent(dbContext, Event.Create(
            EventKind.Task,
            description,
            startsOn: startsOn,
            startsAt: startsAt).Value);
    }

    private static void AddEvent(TaskManagerDbContext dbContext, Event @event)
    {
        dbContext.Events.Add(@event);
    }
}
