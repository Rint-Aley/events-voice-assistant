using System.Text.Json;
using TaskManager.IntegrationTests.Api;
using TaskManager.Turns.Application.Clients.Nlu;

namespace TaskManager.IntegrationTests.E2E;

/// <summary>
/// Сквозные сценарии списка: лимит 3, total_count, порядок с блоком «Без даты»,
/// type_filter и совместимость со старым NLU без type_filter.
/// </summary>
public sealed class E2EListTests(ApiTestFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task List_puts_undated_task_last_and_counts_all_three()
    {
        // Arrange
        await CreateTaskAsync("первая", "2026-10-31", "09:00");
        await CreateTaskAsync("вторая", "2026-10-31", "10:00");
        await CreateTaskAsync("без даты");

        // Act
        Guid turnId = await RunListTurnAsync(
            "что у меня завтра",
            "2026-10-31",
            "2026-10-31");
        JsonElement snapshot = await GetSnapshotAsync(turnId);

        // Assert
        snapshot.GetProperty("state").GetString().Should().Be("COMPLETED");
        snapshot.GetProperty("total_count").GetInt32().Should().Be(3);

        JsonElement events = snapshot.GetProperty("events");
        events.GetArrayLength().Should().Be(3);
        events[0].GetProperty("description").GetString().Should().Be("первая");
        events[1].GetProperty("description").GetString().Should().Be("вторая");
        events[2].GetProperty("description").GetString().Should().Be("без даты");
        events[2].GetProperty("starts_on").ValueKind.Should().Be(JsonValueKind.Null);

        snapshot.GetProperty("answer").GetString()
            .Should().Be("На завтра: 3 записи.");
    }

    [Fact]
    public async Task List_limit_is_three_including_undated_and_total_count_is_before_limit()
    {
        // Arrange
        await CreateTaskAsync("первая", "2026-10-31", "09:00");
        await CreateTaskAsync("вторая", "2026-10-31", "10:00");
        await CreateTaskAsync("третья", "2026-10-31", "11:00");
        await CreateTaskAsync("четвёртая", "2026-10-31", "12:00");
        await CreateTaskAsync("без даты");

        // Act
        Guid turnId = await RunListTurnAsync(
            "что у меня завтра",
            "2026-10-31",
            "2026-10-31");
        JsonElement snapshot = await GetSnapshotAsync(turnId);

        // Assert
        snapshot.GetProperty("total_count").GetInt32().Should().Be(5);

        JsonElement events = snapshot.GetProperty("events");
        events.GetArrayLength().Should().Be(3);
        events.EnumerateArray().Select(item => item.GetProperty("description").GetString())
            .Should().Equal("первая", "вторая", "третья");

        snapshot.GetProperty("answer").GetString()
            .Should().Be("На завтра: 5 записей. И ещё 2 записи.");
    }

    [Fact]
    public async Task Type_filter_separates_meetings_tasks_and_reminders_and_old_nlu_returns_all()
    {
        // Arrange
        await CreateTaskAsync("купить хлеб");
        await CreateReminderAsync("позвонить", "2026-10-31", "10:00");
        await CreateMeetingAsync("планёрка", "2026-10-31", "11:00");

        // Act
        JsonElement meetings = await GetSnapshotAsync(await RunListTurnAsync(
            "какие встречи завтра",
            "2026-10-31",
            "2026-10-31",
            typeFilter: "meeting"));
        JsonElement tasks = await GetSnapshotAsync(await RunListTurnAsync(
            "какие задачи завтра",
            "2026-10-31",
            "2026-10-31",
            typeFilter: "task"));
        JsonElement reminders = await GetSnapshotAsync(await RunListTurnAsync(
            "какие напоминания завтра",
            "2026-10-31",
            "2026-10-31",
            typeFilter: "reminder"));

        // Старый NLU не присылает type_filter: должны вернуться все типы.
        JsonElement all = await GetSnapshotAsync(await RunListTurnAsync(
            "что у меня завтра",
            "2026-10-31",
            "2026-10-31"));

        // Assert
        meetings.GetProperty("total_count").GetInt32().Should().Be(1);
        meetings.GetProperty("events").EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString())
            .Should().Equal("meeting");
        meetings.GetProperty("events")[0].GetProperty("description").GetString()
            .Should().Be("планёрка");

        tasks.GetProperty("total_count").GetInt32().Should().Be(2);
        tasks.GetProperty("events").EnumerateArray()
            .Select(item => item.GetProperty("description").GetString())
            .Should().BeEquivalentTo("купить хлеб", "позвонить");

        reminders.GetProperty("total_count").GetInt32().Should().Be(1);
        reminders.GetProperty("events").EnumerateArray()
            .Select(item => item.GetProperty("description").GetString())
            .Should().Equal("позвонить");

        all.GetProperty("total_count").GetInt32().Should().Be(3);
        all.GetProperty("events").EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString())
            .Should().BeEquivalentTo("task", "reminder", "meeting");
    }

    private Task CreateTaskAsync(
        string description,
        string? startsOn = null,
        string? startsAt = null) =>
        CreateEventThroughPipelineAsync(
            $"создай задачу {description}",
            new NluResponse(
                Guid.Empty,
                "create_task",
                0.9,
                new NluEntities(
                    Description: description,
                    StartsOn: startsOn,
                    StartsAt: startsAt),
                Embedding: Embedding()));

    private Task CreateReminderAsync(string description, string startsOn, string startsAt) =>
        CreateEventThroughPipelineAsync(
            $"напомни {description}",
            new NluResponse(
                Guid.Empty,
                "create_reminder",
                0.9,
                new NluEntities(
                    Description: description,
                    StartsOn: startsOn,
                    StartsAt: startsAt),
                Embedding: Embedding()));

    private Task CreateMeetingAsync(string description, string startsOn, string startsAt) =>
        CreateEventThroughPipelineAsync(
            $"создай встречу {description}",
            new NluResponse(
                Guid.Empty,
                "create_meeting",
                0.9,
                new NluEntities(
                    Description: description,
                    StartsOn: startsOn,
                    StartsAt: startsAt),
                Embedding: Embedding()));
}
