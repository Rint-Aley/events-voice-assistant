using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Infrastructure.Persistence;
using TaskManager.IntegrationTests.Api;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;

namespace TaskManager.IntegrationTests.E2E;

/// <summary>
/// Сквозные сценарии записи: создание всех типов, уточнения, отмена черновика,
/// SESSION_BUSY и ошибка NLU без эмбеддинга.
/// </summary>
public sealed class E2ETurnFlowTests(ApiTestFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Task_reminder_and_meeting_are_created_through_api_and_job_pipeline()
    {
        // Arrange
        var taskResponse = new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: Embedding());
        var reminderResponse = new NluResponse(
            Guid.Empty,
            "create_reminder",
            0.9,
            new NluEntities(
                Description: "позвонить Ивану",
                StartsOn: "2026-10-31",
                StartsAt: "09:00"),
            Embedding: Embedding());
        var meetingResponse = new NluResponse(
            Guid.Empty,
            "create_meeting",
            0.9,
            new NluEntities(
                Description: "созвон с командой",
                StartsOn: "2026-10-31",
                StartsAt: "11:00"),
            Embedding: Embedding());

        // Act
        Guid taskTurnId = await CreateEventThroughPipelineAsync("купи хлеб", taskResponse);
        Guid reminderTurnId = await CreateEventThroughPipelineAsync(
            "напомни завтра в 9 позвонить Ивану",
            reminderResponse);
        Guid meetingTurnId = await CreateEventThroughPipelineAsync(
            "создай встречу завтра в 11",
            meetingResponse);

        // Assert
        JsonElement task = await GetSnapshotAsync(taskTurnId);
        task.GetProperty("state").GetString().Should().Be("COMPLETED");
        task.GetProperty("answer").GetString()
            .Should().Be("Записала задачу: «купить хлеб».");
        task.GetProperty("events").GetArrayLength().Should().Be(1);
        task.GetProperty("events")[0].GetProperty("kind").GetString().Should().Be("task");
        task.GetProperty("events")[0].GetProperty("description").GetString()
            .Should().Be("купить хлеб");
        task.GetProperty("events")[0].GetProperty("starts_on").ValueKind
            .Should().Be(JsonValueKind.Null);
        task.GetProperty("events")[0].GetProperty("starts_at").ValueKind
            .Should().Be(JsonValueKind.Null);
        task.GetProperty("events")[0].GetProperty("is_deadline").GetBoolean()
            .Should().BeFalse();

        JsonElement reminder = await GetSnapshotAsync(reminderTurnId);
        reminder.GetProperty("state").GetString().Should().Be("COMPLETED");
        reminder.GetProperty("answer").GetString()
            .Should().Be("Напомню: «позвонить Ивану» — завтра, 09:00.");
        reminder.GetProperty("events")[0].GetProperty("kind").GetString()
            .Should().Be("reminder");
        reminder.GetProperty("events")[0].GetProperty("starts_on").GetString()
            .Should().Be("2026-10-31");
        reminder.GetProperty("events")[0].GetProperty("starts_at").GetString()
            .Should().Be("09:00");

        JsonElement meeting = await GetSnapshotAsync(meetingTurnId);
        meeting.GetProperty("state").GetString().Should().Be("COMPLETED");
        meeting.GetProperty("answer").GetString()
            .Should().Be("Записала встречу: «созвон с командой» — завтра, 11:00.");
        meeting.GetProperty("events")[0].GetProperty("kind").GetString()
            .Should().Be("meeting");
        meeting.GetProperty("events")[0].GetProperty("starts_on").GetString()
            .Should().Be("2026-10-31");
        meeting.GetProperty("events")[0].GetProperty("starts_at").GetString()
            .Should().Be("11:00");

        int storedEvents = await WithScopeAsync(provider =>
            provider.GetRequiredService<TaskManagerDbContext>().Events.CountAsync());
        storedEvents.Should().Be(3);
    }

    [Fact]
    public async Task Clarification_keeps_draft_for_two_rounds_then_completes_without_event()
    {
        // Arrange
        Guid firstTurnId = await CreateTextTurnAsync("создай встречу");
        Fixture.Nlu.Respond = _ => ClarifyResponse("поездка");

        // Act: первый раунд
        Result<InterpretTurnResult> first = await RunNluJobAsync(firstTurnId);
        JsonElement firstSnapshot = await GetSnapshotAsync(firstTurnId);

        // Assert: первый вопрос и сохранённый черновик
        first.IsSuccess.Should().BeTrue();
        first.Value.State.Should().Be(TurnState.NeedsClarification);
        firstSnapshot.GetProperty("state").GetString().Should().Be("NEEDS_CLARIFICATION");
        firstSnapshot.GetProperty("answer").GetString()
            .Should().Be("На какой день поставить «поездка»?");
        (await DraftRoundAsync(firstTurnId)).Should().Be(1);

        // Act: второй раунд
        Guid secondTurnId = await CreateTextTurnAsync("в пятницу");
        Result<InterpretTurnResult> second = await RunNluJobAsync(secondTurnId);

        // Assert: вопрос повторён, черновик заменён
        second.IsSuccess.Should().BeTrue();
        second.Value.State.Should().Be(TurnState.NeedsClarification);
        (await DraftRoundAsync(secondTurnId)).Should().Be(2);

        // Act: ответ после двух раундов
        Guid thirdTurnId = await CreateTextTurnAsync("всё равно не понимаю");
        Result<InterpretTurnResult> third = await RunNluJobAsync(thirdTurnId);
        JsonElement thirdSnapshot = await GetSnapshotAsync(thirdTurnId);

        // Assert: ход завершён, черновик удалён, событие не создано
        third.IsSuccess.Should().BeTrue();
        third.Value.State.Should().Be(TurnState.Completed);
        thirdSnapshot.GetProperty("state").GetString().Should().Be("COMPLETED");
        thirdSnapshot.GetProperty("answer").GetString()
            .Should().Be("Не удалось уточнить. Сформулируй запрос иначе.");
        (await DraftCountAsync()).Should().Be(0);
        (await EventCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Draft_cancel_is_idempotent_through_api_and_keeps_turn_state()
    {
        // Arrange
        Guid turnId = await CreateTextTurnAsync("создай встречу");
        Fixture.Nlu.Respond = _ => ClarifyResponse("поездка");
        await RunNluJobAsync(turnId);
        (await DraftCountAsync()).Should().Be(1);

        // Act
        HttpResponseMessage first = await Client.DeleteAsync($"/api/v1/turns/{turnId}/draft");
        HttpResponseMessage repeat = await Client.DeleteAsync($"/api/v1/turns/{turnId}/draft");
        HttpResponseMessage unknown = await Client.DeleteAsync(
            $"/api/v1/turns/{Guid.NewGuid()}/draft");

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument firstBody = await ReadJsonAsync(first);
        firstBody.RootElement.GetProperty("text").GetString()
            .Should().Be("Хорошо, не сохраняю");

        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument repeatBody = await ReadJsonAsync(repeat);
        repeatBody.RootElement.GetProperty("text").GetString()
            .Should().Be("Хорошо, не сохраняю");

        (await DraftCountAsync()).Should().Be(0);
        (await GetSnapshotAsync(turnId)).GetProperty("state").GetString()
            .Should().Be("NEEDS_CLARIFICATION");

        await AssertErrorAsync(unknown, HttpStatusCode.NotFound, "TURN_NOT_FOUND");
    }

    [Fact]
    public async Task Second_message_while_turn_is_active_returns_409_and_works_after_completion()
    {
        // Arrange
        Guid firstTurnId = await CreateTextTurnAsync("купи хлеб");

        // Act
        HttpResponseMessage busy = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                idempotency_key = Guid.NewGuid().ToString(),
                text = "ещё задача",
                object_key = (string?)null,
            });

        // Assert
        await AssertErrorAsync(busy, HttpStatusCode.Conflict, "SESSION_BUSY");

        // Act: завершаем первый ход и отправляем следующий
        Fixture.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: Embedding());
        await RunNluJobAsync(firstTurnId);
        await RunApplyJobAsync(firstTurnId);

        HttpResponseMessage next = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                idempotency_key = Guid.NewGuid().ToString(),
                text = "помыть посуду",
                object_key = (string?)null,
            });

        // Assert
        next.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using JsonDocument nextBody = await ReadJsonAsync(next);
        nextBody.RootElement.GetProperty("turn_id").GetGuid().Should().NotBe(firstTurnId);
    }

    [Fact]
    public async Task Description_without_embedding_fails_turn_with_nlu_failed_and_creates_no_event()
    {
        // Arrange
        Guid turnId = await CreateTextTurnAsync("купи хлеб");
        Fixture.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: null);

        // Act
        Result<InterpretTurnResult> interpreted = await RunNluJobAsync(turnId);
        JsonElement snapshot = await GetSnapshotAsync(turnId);

        // Assert
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.State.Should().Be(TurnState.Failed);

        snapshot.GetProperty("state").GetString().Should().Be("FAILED");
        snapshot.GetProperty("answer").ValueKind.Should().Be(JsonValueKind.Null);
        snapshot.GetProperty("events").GetArrayLength().Should().Be(0);
        snapshot.GetProperty("error").GetProperty("code").GetString().Should().Be("NLU_FAILED");
        snapshot.GetProperty("error").GetProperty("message").GetString()
            .Should().Be("Не удалось разобрать запрос. Сформулируй его иначе.");

        (await EventCountAsync()).Should().Be(0);
    }

    private static NluResponse ClarifyResponse(string description) => new(
        Guid.Empty,
        "clarify",
        0.8,
        new NluEntities(Description: description),
        Missing: ["date"],
        Question: $"На какой день поставить «{description}»?",
        Embedding: Embedding());

    private Task<int> DraftCountAsync() =>
        WithScopeAsync(provider =>
            provider.GetRequiredService<TaskManagerDbContext>().Drafts.CountAsync());

    private Task<int> DraftRoundAsync(Guid turnId) =>
        WithScopeAsync(provider =>
            provider.GetRequiredService<TaskManagerDbContext>().Drafts
                .Where(draft => draft.TurnId == turnId)
                .Select(draft => draft.ClarificationRound)
                .SingleAsync());

    private Task<int> EventCountAsync() =>
        WithScopeAsync(provider =>
            provider.GetRequiredService<TaskManagerDbContext>().Events.CountAsync());
}
