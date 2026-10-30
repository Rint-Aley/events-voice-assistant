using Microsoft.EntityFrameworkCore;
using TaskManager.Events.Domain;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Infrastructure.Persistence.Queries;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Persistence;

public sealed class EventQueryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ListActiveMeetingCandidates_includes_meeting_started_the_day_before_window()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var overnightMeeting = AddMeeting(
            dbContext,
            "Ночной созвон",
            new DateOnly(2026, 10, 30),
            new TimeOnly(23, 30),
            TimeSpan.FromMinutes(180));
        await dbContext.SaveChangesAsync();

        var candidate = Event.Create(
            EventKind.Meeting,
            "Утренний созвон",
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(0, 30),
            duration: TimeSpan.FromMinutes(60)).Value;

        var query = new EventQuery(dbContext);

        // Act
        var existing = await query.ListActiveMeetingCandidatesAsync(
            new DateTime(2026, 10, 31, 0, 30, 0),
            new DateTime(2026, 10, 31, 1, 30, 0));

        // Assert
        existing.Should().ContainSingle(@event => @event.Id == overnightMeeting.Id);
        ConflictDetector.Detect(candidate, existing)
            .Should().ContainSingle()
            .Which.Id.Should().Be(overnightMeeting.Id);
    }

    [Fact]
    public async Task ListActiveMeetingCandidates_returns_superset_that_detector_filters()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var morningMeeting = AddMeeting(
            dbContext,
            "Утренняя встреча",
            new DateOnly(2026, 10, 31),
            new TimeOnly(8, 0),
            TimeSpan.FromMinutes(60));
        await dbContext.SaveChangesAsync();

        var candidate = Event.Create(
            EventKind.Meeting,
            "Вечерняя встреча",
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(15, 0),
            duration: TimeSpan.FromMinutes(60)).Value;

        var query = new EventQuery(dbContext);

        // Act
        var existing = await query.ListActiveMeetingCandidatesAsync(
            new DateTime(2026, 10, 31, 15, 0, 0),
            new DateTime(2026, 10, 31, 16, 0, 0));

        // Assert
        existing.Should().ContainSingle(@event => @event.Id == morningMeeting.Id);
        ConflictDetector.Detect(candidate, existing).Should().BeEmpty();
    }

    [Fact]
    public async Task ListActiveMeetingCandidates_excludes_meetings_started_before_previous_day()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddMeeting(
            dbContext,
            "Позапрошлая встреча",
            new DateOnly(2026, 10, 29),
            new TimeOnly(10, 0),
            TimeSpan.FromMinutes(60));
        await dbContext.SaveChangesAsync();

        var query = new EventQuery(dbContext);

        // Act
        var existing = await query.ListActiveMeetingCandidatesAsync(
            new DateTime(2026, 10, 31, 15, 0, 0),
            new DateTime(2026, 10, 31, 16, 0, 0));

        // Assert
        existing.Should().BeEmpty();
    }

    [Fact]
    public async Task ListActiveMeetingCandidates_takes_the_longest_meeting_of_previous_day()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var longestMeeting = AddMeeting(
            dbContext,
            "Сутки с 23:30",
            new DateOnly(2026, 10, 30),
            new TimeOnly(23, 30),
            TimeSpan.FromMinutes(1440));
        await dbContext.SaveChangesAsync();

        var candidate = Event.Create(
            EventKind.Meeting,
            "Ночная встреча",
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(22, 0),
            duration: TimeSpan.FromMinutes(60)).Value;

        var query = new EventQuery(dbContext);

        // Act
        var existing = await query.ListActiveMeetingCandidatesAsync(
            new DateTime(2026, 10, 31, 22, 0, 0),
            new DateTime(2026, 10, 31, 23, 0, 0));

        // Assert
        existing.Should().ContainSingle(@event => @event.Id == longestMeeting.Id);
        ConflictDetector.Detect(candidate, existing)
            .Should().ContainSingle()
            .Which.Id.Should().Be(longestMeeting.Id);
    }

    [Fact]
    public async Task ListActiveMeetingCandidates_excludes_cancelled_done_and_non_meetings()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddMeeting(
            dbContext,
            "Активная встреча",
            new DateOnly(2026, 10, 31),
            new TimeOnly(15, 0),
            TimeSpan.FromMinutes(60));
        AddEvent(dbContext, Event.Restore(
            Guid.NewGuid(),
            EventKind.Meeting,
            EventStatus.Cancelled,
            "Отменённая встреча",
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(15, 0),
            duration: TimeSpan.FromMinutes(60)).Value);
        AddEvent(dbContext, Event.Restore(
            Guid.NewGuid(),
            EventKind.Meeting,
            EventStatus.Done,
            "Завершённая встреча",
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(15, 0),
            duration: TimeSpan.FromMinutes(60)).Value);
        AddEvent(dbContext, Event.Create(
            EventKind.Reminder,
            "Напоминание",
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(15, 0)).Value);
        await dbContext.SaveChangesAsync();

        var query = new EventQuery(dbContext);

        // Act
        var existing = await query.ListActiveMeetingCandidatesAsync(
            new DateTime(2026, 10, 31, 15, 0, 0),
            new DateTime(2026, 10, 31, 16, 0, 0));

        // Assert
        existing.Should().ContainSingle()
            .Which.Description.Should().Be("Активная встреча");
    }

    [Fact]
    public async Task ListActiveMeetingCandidates_returns_empty_for_reversed_window()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddMeeting(
            dbContext,
            "Встреча",
            new DateOnly(2026, 10, 31),
            new TimeOnly(15, 0),
            TimeSpan.FromMinutes(60));
        await dbContext.SaveChangesAsync();

        var query = new EventQuery(dbContext);

        // Act
        var existing = await query.ListActiveMeetingCandidatesAsync(
            new DateTime(2026, 10, 31, 16, 0, 0),
            new DateTime(2026, 10, 31, 15, 0, 0));

        // Assert
        existing.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPeriod_counts_all_visible_events_before_limit()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddTask(dbContext, "Первая", new DateOnly(2026, 10, 30), new TimeOnly(9, 0));
        AddTask(dbContext, "Вторая", new DateOnly(2026, 10, 30), new TimeOnly(10, 0));
        AddTask(dbContext, "Без даты");
        await dbContext.SaveChangesAsync();

        var query = new EventQuery(dbContext);

        // Act
        var result = await query.ListPeriodAsync(
            new DateOnly(2026, 10, 30),
            new DateOnly(2026, 10, 30),
            typeFilter: null,
            limit: 2);

        // Assert
        result.TotalCount.Should().Be(3);
        result.Events.Select(@event => @event.Description)
            .Should().Equal("Первая", "Вторая");
    }

    [Fact]
    public async Task ListPeriod_puts_events_without_date_last_and_keeps_order_stable()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddTask(dbContext, "Без даты", id: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        AddTask(dbContext, "Днём", new DateOnly(2026, 10, 30), new TimeOnly(12, 0));
        AddTask(dbContext, "Без времени", new DateOnly(2026, 10, 30));
        await dbContext.SaveChangesAsync();

        var query = new EventQuery(dbContext);

        // Act
        var result = await query.ListPeriodAsync(
            new DateOnly(2026, 10, 30),
            new DateOnly(2026, 10, 30),
            typeFilter: null,
            limit: 10);

        // Assert
        result.Events.Select(@event => @event.Description)
            .Should().Equal("Днём", "Без времени", "Без даты");
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

    private static Event AddMeeting(
        TaskManagerDbContext dbContext,
        string description,
        DateOnly startsOn,
        TimeOnly startsAt,
        TimeSpan duration)
    {
        var @event = Event.Create(
            EventKind.Meeting,
            description,
            startsOn: startsOn,
            startsAt: startsAt,
            duration: duration).Value;

        dbContext.Events.Add(@event);

        return @event;
    }

    private static Event AddTask(
        TaskManagerDbContext dbContext,
        string description,
        DateOnly? startsOn = null,
        TimeOnly? startsAt = null,
        Guid? id = null)
    {
        var @event = Event.Create(
            EventKind.Task,
            description,
            startsOn: startsOn,
            startsAt: startsAt,
            id: id).Value;

        dbContext.Events.Add(@event);

        return @event;
    }

    private static void AddEvent(TaskManagerDbContext dbContext, Event @event)
    {
        dbContext.Events.Add(@event);
    }
}
