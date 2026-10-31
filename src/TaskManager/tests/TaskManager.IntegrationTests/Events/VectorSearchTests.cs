using Microsoft.EntityFrameworkCore;
using Pgvector;
using TaskManager.Events.Application.Queries;
using TaskManager.Events.Application.Scenarios;
using TaskManager.Events.Domain;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Infrastructure.Persistence.Queries;
using Testcontainers.PostgreSql;

namespace TaskManager.IntegrationTests.Events;

/// <summary>
/// Семантический поиск проверяется на реальном Postgres/pgvector: HNSW-индекс,
/// косинусная близость и колонка <c>vector(1536)</c>.
/// </summary>
public sealed class VectorSearchTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 11, 1);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:0.8.7-pg17")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Search_returns_top_three_by_cosine_distance_and_excludes_cancelled()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();

        Event closest = AddEmbedded(dbContext, "Позвонить маме", EventKind.Task, Vector(1f));
        Event second = AddEmbedded(
            dbContext,
            "Купить хлеб",
            EventKind.Task,
            Vector(1f, 0.1f),
            status: EventStatus.Done);
        Event third = AddEmbedded(
            dbContext,
            "Собрать отчёт",
            EventKind.Reminder,
            Vector(1f, 0.2f),
            startsOn: Day,
            startsAt: new TimeOnly(9, 0));
        AddEmbedded(
            dbContext,
            "Встреча с Иваном",
            EventKind.Meeting,
            Vector(0f, 1f),
            startsOn: Day,
            startsAt: new TimeOnly(10, 0));
        AddEmbedded(dbContext, "Убрать квартиру", EventKind.Task, Vector(0f, 0f, 1f));
        AddEmbedded(
            dbContext,
            "Отменённый звонок",
            EventKind.Task,
            Vector(1f),
            status: EventStatus.Cancelled);
        await dbContext.SaveChangesAsync();

        var handler = new SearchHandler(new EventQuery(dbContext));

        // Act
        var result = await handler.HandleAsync(new SearchQuery(Vector(1f)));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeFalse();
        result.Value.Events.Select(@event => @event.Id)
            .Should().Equal(closest.Id, second.Id, third.Id);
        result.Value.Events.Select(@event => @event.Description)
            .Should().NotContain("Отменённый звонок");
    }

    [Fact]
    public async Task Search_applies_type_filter_with_task_including_reminders()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();

        AddEmbedded(dbContext, "Задача", EventKind.Task, Vector(1f));
        AddEmbedded(
            dbContext,
            "Напоминание",
            EventKind.Reminder,
            Vector(1f),
            startsOn: Day,
            startsAt: new TimeOnly(9, 0));
        AddEmbedded(
            dbContext,
            "Встреча",
            EventKind.Meeting,
            Vector(1f),
            startsOn: Day,
            startsAt: new TimeOnly(10, 0));
        await dbContext.SaveChangesAsync();

        var handler = new SearchHandler(new EventQuery(dbContext));

        // Act
        var meetings = await handler.HandleAsync(
            new SearchQuery(Vector(1f), EventTypeFilter.Meeting));
        var tasks = await handler.HandleAsync(
            new SearchQuery(Vector(1f), EventTypeFilter.Task));
        var reminders = await handler.HandleAsync(
            new SearchQuery(Vector(1f), EventTypeFilter.Reminder));

        // Assert
        meetings.IsSuccess.Should().BeTrue();
        meetings.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Встреча");

        tasks.IsSuccess.Should().BeTrue();
        tasks.Value.Events.Select(@event => @event.Description)
            .Should().BeEquivalentTo("Задача", "Напоминание");

        reminders.IsSuccess.Should().BeTrue();
        reminders.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Напоминание");
    }

    [Fact]
    public async Task Search_applies_optional_period_by_starts_on_inclusively()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();

        AddEmbedded(
            dbContext,
            "Первого",
            EventKind.Task,
            Vector(1f),
            startsOn: Day);
        AddEmbedded(
            dbContext,
            "Пятого",
            EventKind.Task,
            Vector(1f),
            startsOn: Day.AddDays(4));
        AddEmbedded(dbContext, "Без даты", EventKind.Task, Vector(1f));
        await dbContext.SaveChangesAsync();

        var handler = new SearchHandler(new EventQuery(dbContext));

        // Act
        var exactDay = await handler.HandleAsync(
            new SearchQuery(Vector(1f), From: Day, To: Day));
        var fromOnly = await handler.HandleAsync(
            new SearchQuery(Vector(1f), From: Day.AddDays(4)));
        var toOnly = await handler.HandleAsync(
            new SearchQuery(Vector(1f), To: Day));
        var withoutPeriod = await handler.HandleAsync(new SearchQuery(Vector(1f)));

        // Assert
        exactDay.IsSuccess.Should().BeTrue();
        exactDay.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Первого");

        fromOnly.IsSuccess.Should().BeTrue();
        fromOnly.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Пятого");

        toOnly.IsSuccess.Should().BeTrue();
        toOnly.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Первого");

        withoutPeriod.IsSuccess.Should().BeTrue();
        withoutPeriod.Value.Events.Select(@event => @event.Description)
            .Should().BeEquivalentTo("Первого", "Пятого", "Без даты");
    }

    [Fact]
    public async Task Search_combines_type_filter_with_period()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();

        AddEmbedded(
            dbContext,
            "Задача в периоде",
            EventKind.Task,
            Vector(1f),
            startsOn: Day);
        AddEmbedded(
            dbContext,
            "Встреча в периоде",
            EventKind.Meeting,
            Vector(1f),
            startsOn: Day,
            startsAt: new TimeOnly(10, 0));
        AddEmbedded(
            dbContext,
            "Задача вне периода",
            EventKind.Task,
            Vector(1f),
            startsOn: Day.AddDays(30));
        await dbContext.SaveChangesAsync();

        var handler = new SearchHandler(new EventQuery(dbContext));

        // Act
        var result = await handler.HandleAsync(
            new SearchQuery(Vector(1f), EventTypeFilter.Task, Day, Day));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Задача в периоде");
    }

    [Fact]
    public async Task Search_returns_at_most_three_records_with_type_filter()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();

        AddEmbedded(dbContext, "Задача 1", EventKind.Task, Vector(1f));
        AddEmbedded(dbContext, "Задача 2", EventKind.Task, Vector(1f, 0.1f));
        AddEmbedded(dbContext, "Задача 3", EventKind.Task, Vector(1f, 0.2f));
        AddEmbedded(dbContext, "Задача 4", EventKind.Task, Vector(1f, 0.3f));
        AddEmbedded(dbContext, "Задача 5", EventKind.Task, Vector(1f, 0.4f));
        await dbContext.SaveChangesAsync();

        var handler = new SearchHandler(new EventQuery(dbContext));

        // Act
        var result = await handler.HandleAsync(
            new SearchQuery(Vector(1f), EventTypeFilter.Task));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Events.Select(@event => @event.Description)
            .Should().Equal("Задача 1", "Задача 2", "Задача 3");
    }

    [Fact]
    public async Task Search_returns_empty_success_when_nothing_matches()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();

        AddEmbedded(
            dbContext,
            "Отменённая запись",
            EventKind.Task,
            Vector(1f),
            status: EventStatus.Cancelled);
        AddEvent(dbContext, Event.Create(EventKind.Task, "Без embedding").Value);
        await dbContext.SaveChangesAsync();

        var handler = new SearchHandler(new EventQuery(dbContext));

        // Act
        var result = await handler.HandleAsync(new SearchQuery(Vector(1f)));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
        result.Value.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1535)]
    [InlineData(1537)]
    public async Task Search_rejects_embedding_with_wrong_dimension(int length)
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var handler = new SearchHandler(new EventQuery(dbContext));
        float[] embedding = Enumerable.Repeat(0.1f, length).ToArray();

        // Act
        var result = await handler.HandleAsync(new SearchQuery(embedding));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_EMBEDDING");
    }

    [Fact]
    public async Task Search_rejects_null_embedding()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var handler = new SearchHandler(new EventQuery(dbContext));

        // Act
        var result = await handler.HandleAsync(new SearchQuery(null!));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_EMBEDDING");
    }

    [Fact]
    public async Task Search_rejects_non_finite_embedding_values()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        var handler = new SearchHandler(new EventQuery(dbContext));
        float[] embedding = Enumerable.Repeat(0.1f, EventEmbedding.Dimensions).ToArray();
        embedding[10] = float.NaN;

        // Act
        var result = await handler.HandleAsync(new SearchQuery(embedding));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_EMBEDDING");
    }

    [Fact]
    public async Task Search_rejects_reversed_period()
    {
        // Arrange
        await using var dbContext = await CreateDbContextAsync();
        AddEmbedded(dbContext, "Задача", EventKind.Task, Vector(1f), startsOn: Day);
        await dbContext.SaveChangesAsync();

        var handler = new SearchHandler(new EventQuery(dbContext));

        // Act
        var result = await handler.HandleAsync(
            new SearchQuery(Vector(1f), From: Day, To: Day.AddDays(-1)));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("INVALID_PERIOD");
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

    private static Event AddEmbedded(
        TaskManagerDbContext dbContext,
        string description,
        EventKind kind,
        float[] embedding,
        EventStatus status = EventStatus.Active,
        DateOnly? startsOn = null,
        TimeOnly? startsAt = null)
    {
        var created = status == EventStatus.Active
            ? Event.Create(kind, description, startsOn: startsOn, startsAt: startsAt)
            : Event.Restore(
                Guid.NewGuid(),
                kind,
                status,
                description,
                startsOn: startsOn,
                startsAt: startsAt);

        Event @event = created.Value;
        dbContext.Events.Add(@event);

        // Embedding — теневое свойство Infrastructure: Domain не знает про pgvector.
        var entry = dbContext.Entry(@event);
        entry.Property("Embedding").CurrentValue = new Vector(embedding);
        entry.Property("EmbeddingModel").CurrentValue = EventEmbedding.Model;

        return @event;
    }

    private static void AddEvent(TaskManagerDbContext dbContext, Event @event)
    {
        dbContext.Events.Add(@event);
    }

    private static float[] Vector(float first, float second = 0f, float third = 0f)
    {
        var values = new float[EventEmbedding.Dimensions];
        values[0] = first;
        values[1] = second;
        values[2] = third;

        return values;
    }
}
