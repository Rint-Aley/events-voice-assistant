using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Application.Abstractions;
using TaskManager.Events.Domain;
using TaskManager.Infrastructure.Persistence;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;

namespace TaskManager.IntegrationTests.Api;

public sealed class ApiEndpointTests(ApiTestFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Uploads_returns_presigned_put_and_rejects_bad_requests()
    {
        // Arrange / Act
        HttpResponseMessage created = await PostJsonAsync(
            "/api/v1/uploads",
            new { content_type = "audio/webm", size_bytes = 42_000 });
        HttpResponseMessage unsupported = await PostJsonAsync(
            "/api/v1/uploads",
            new { content_type = "audio/avi", size_bytes = 42_000 });
        HttpResponseMessage tooLarge = await PostJsonAsync(
            "/api/v1/uploads",
            new { content_type = "audio/webm", size_bytes = 10 * 1024 * 1024 + 1 });
        HttpResponseMessage missing = await PostJsonAsync("/api/v1/uploads", new { });

        // Assert
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        using JsonDocument createdBody = await ReadJsonAsync(created);
        createdBody.RootElement.GetProperty("object_key").GetString().Should().Be("audio/fake.webm");
        createdBody.RootElement.GetProperty("upload_url").GetString()
            .Should().Be(FakeObjectStorage.UploadUrl);
        createdBody.RootElement.TryGetProperty("expires_at", out _).Should().BeTrue();

        await AssertErrorAsync(unsupported, HttpStatusCode.BadRequest, "UNSUPPORTED_AUDIO");
        await AssertErrorAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, "PAYLOAD_TOO_LARGE");
        await AssertErrorAsync(missing, HttpStatusCode.BadRequest, "BAD_REQUEST");
    }

    [Fact]
    public async Task Messages_creates_text_turn_returns_202_and_is_idempotent()
    {
        // Arrange
        string idempotencyKey = Guid.NewGuid().ToString();

        // Act
        HttpResponseMessage first = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                session_id = Guid.NewGuid(),
                idempotency_key = idempotencyKey,
                text = "создай задачу",
                object_key = (string?)null,
            });
        HttpResponseMessage repeat = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                idempotency_key = idempotencyKey,
                text = "другой текст",
                object_key = (string?)null,
            });

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using JsonDocument firstBody = await ReadJsonAsync(first);
        Guid turnId = firstBody.RootElement.GetProperty("turn_id").GetGuid();
        firstBody.RootElement.GetProperty("state").GetString().Should().Be("INTERPRETING");
        firstBody.RootElement.GetProperty("events_url").GetString()
            .Should().Be($"/api/v1/turns/{turnId}/events");

        repeat.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using JsonDocument repeatBody = await ReadJsonAsync(repeat);
        repeatBody.RootElement.GetProperty("turn_id").GetGuid().Should().Be(turnId);

        Fixture.Scheduler.Scheduled.Should().ContainSingle()
            .Which.Should().Be((JobKind.Nlu, turnId));

        HttpResponseMessage snapshot = await Client.GetAsync($"/api/v1/turns/{turnId}");
        snapshot.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument snapshotBody = await ReadJsonAsync(snapshot);
        snapshotBody.RootElement.GetProperty("state").GetString().Should().Be("INTERPRETING");
        snapshotBody.RootElement.GetProperty("text_input").GetString().Should().Be("создай задачу");
        snapshotBody.RootElement.GetProperty("answer").ValueKind.Should().Be(JsonValueKind.Null);
        snapshotBody.RootElement.GetProperty("events").GetArrayLength().Should().Be(0);

        HttpResponseMessage busy = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                idempotency_key = Guid.NewGuid().ToString(),
                text = "ещё задача",
                object_key = (string?)null,
            });
        await AssertErrorAsync(busy, HttpStatusCode.Conflict, "SESSION_BUSY");
    }

    [Fact]
    public async Task Messages_accepts_voice_and_schedules_stt()
    {
        // Act
        HttpResponseMessage response = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                idempotency_key = Guid.NewGuid().ToString(),
                text = (string?)null,
                object_key = "audio/demo/voice.webm",
            });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using JsonDocument body = await ReadJsonAsync(response);
        Guid turnId = body.RootElement.GetProperty("turn_id").GetGuid();
        body.RootElement.GetProperty("state").GetString().Should().Be("TRANSCRIBING");

        Fixture.Scheduler.Scheduled.Should().ContainSingle()
            .Which.Should().Be((JobKind.Stt, turnId));
    }

    [Theory]
    [InlineData("""{"idempotency_key":"00000000-0000-0000-0000-000000000002","text":"a","object_key":"audio/x.webm"}""", HttpStatusCode.BadRequest, "BAD_REQUEST")]
    [InlineData("""{"idempotency_key":"00000000-0000-0000-0000-000000000003"}""", HttpStatusCode.BadRequest, "BAD_REQUEST")]
    [InlineData("""{"idempotency_key":"00000000-0000-0000-0000-000000000004","text":" "}""", HttpStatusCode.BadRequest, "BAD_REQUEST")]
    [InlineData("""{"idempotency_key":"not-a-uuid","text":"купить хлеб"}""", HttpStatusCode.BadRequest, "BAD_REQUEST")]
    [InlineData("""{ not-json }""", HttpStatusCode.BadRequest, "BAD_REQUEST")]
    public async Task Messages_rejects_invalid_payloads(
        string payload,
        HttpStatusCode status,
        string code)
    {
        // Act
        HttpResponseMessage response = await Client.PostAsync(
            "/api/v1/messages",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        // Assert
        await AssertErrorAsync(response, status, code);
    }

    [Fact]
    public async Task Messages_rejects_non_json_content_type()
    {
        // Arrange
        var content = new StringContent(
            """{"idempotency_key":"00000000-0000-0000-0000-000000000009","text":"купить хлеб"}""",
            Encoding.UTF8,
            "text/plain");

        // Act
        HttpResponseMessage response = await Client.PostAsync("/api/v1/messages", content);

        // Assert
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "BAD_REQUEST");
    }

    [Fact]
    public async Task Messages_rejects_text_over_limit()
    {
        // Act
        HttpResponseMessage response = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                idempotency_key = Guid.NewGuid().ToString(),
                text = new string('я', 1001),
            });

        // Assert
        await AssertErrorAsync(response, HttpStatusCode.RequestEntityTooLarge, "PAYLOAD_TOO_LARGE");
    }

    [Fact]
    public async Task Draft_cancel_is_idempotent_and_unknown_turn_is_404()
    {
        // Arrange
        Guid turnId = await CreateTextTurnAsync("создай встречу");
        Fixture.Nlu.Respond = request => new NluResponse(
            request.TurnId,
            "create_meeting",
            0.9,
            new NluEntities(Description: "встреча с командой"),
            Embedding: new NluEmbedding(
                EventEmbedding.Model,
                EventEmbedding.Dimensions,
                Enumerable.Repeat(0.1, EventEmbedding.Dimensions).ToArray()));

        InterpretTurnResult interpreted = await WithScopeAsync(async provider =>
        {
            var handler = provider.GetRequiredService<InterpretTurnHandler>();
            var result = await handler.HandleAsync(turnId);

            result.IsSuccess.Should().BeTrue();
            return result.Value;
        });

        (await DraftCountAsync()).Should().Be(1);

        // Act
        HttpResponseMessage first = await Client.DeleteAsync($"/api/v1/turns/{turnId}/draft");
        HttpResponseMessage repeat = await Client.DeleteAsync($"/api/v1/turns/{turnId}/draft");
        HttpResponseMessage unknown = await Client.DeleteAsync(
            $"/api/v1/turns/{Guid.NewGuid()}/draft");

        // Assert
        interpreted.State.Should().Be(TurnState.NeedsClarification);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument firstBody = await ReadJsonAsync(first);
        firstBody.RootElement.GetProperty("text").GetString().Should().Be("Хорошо, не сохраняю");

        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument repeatBody = await ReadJsonAsync(repeat);
        repeatBody.RootElement.GetProperty("text").GetString().Should().Be("Хорошо, не сохраняю");

        (await DraftCountAsync()).Should().Be(0);
        (await TurnStateAsync(turnId)).Should().Be(TurnState.NeedsClarification);

        await AssertErrorAsync(unknown, HttpStatusCode.NotFound, "TURN_NOT_FOUND");
    }

    [Fact]
    public async Task Old_nlu_response_without_type_filter_and_is_deadline_works()
    {
        // Arrange
        Guid turnId = await CreateTextTurnAsync("купи хлеб");
        Fixture.Nlu.Respond = request => new NluResponse(
            request.TurnId,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: new NluEmbedding(
                EventEmbedding.Model,
                EventEmbedding.Dimensions,
                Enumerable.Repeat(0.1, EventEmbedding.Dimensions).ToArray()));

        // Act
        await WithScopeAsync(async provider =>
        {
            var interpret = provider.GetRequiredService<InterpretTurnHandler>();
            var apply = provider.GetRequiredService<ApplyTurnPlanHandler>();

            var interpreted = await interpret.HandleAsync(turnId);
            interpreted.IsSuccess.Should().BeTrue();
            interpreted.Value.State.Should().Be(TurnState.Applying);

            var applied = await apply.HandleAsync(turnId);
            applied.IsSuccess.Should().BeTrue();
        });

        // Assert
        HttpResponseMessage snapshot = await Client.GetAsync($"/api/v1/turns/{turnId}");
        snapshot.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(snapshot);
        body.RootElement.GetProperty("state").GetString().Should().Be("COMPLETED");
        body.RootElement.GetProperty("answer").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("events").GetArrayLength().Should().Be(1);
        body.RootElement.GetProperty("events")[0].GetProperty("kind").GetString()
            .Should().Be("task");
        body.RootElement.GetProperty("events")[0].GetProperty("is_deadline").GetBoolean()
            .Should().BeFalse();
        body.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Voice_turn_is_transcribed_through_fake_stt()
    {
        // Arrange
        HttpResponseMessage submitted = await PostJsonAsync(
            "/api/v1/messages",
            new
            {
                idempotency_key = Guid.NewGuid().ToString(),
                object_key = "audio/demo/voice.webm",
            });
        using JsonDocument submittedBody = await ReadJsonAsync(submitted);
        Guid turnId = submittedBody.RootElement.GetProperty("turn_id").GetGuid();

        Fixture.Stt.Respond = request => new SttResult(
            request.TurnId,
            "создай задачу",
            "ru",
            0.92m,
            Empty: false,
            DurationMs: 1200,
            Provider: "fake");

        // Act
        await WithScopeAsync(async provider =>
        {
            var transcribe = provider.GetRequiredService<TranscribeTurnHandler>();
            var result = await transcribe.HandleAsync(turnId);
            result.IsSuccess.Should().BeTrue();
        });

        // Assert
        HttpResponseMessage snapshot = await Client.GetAsync($"/api/v1/turns/{turnId}");
        using JsonDocument body = await ReadJsonAsync(snapshot);
        body.RootElement.GetProperty("state").GetString().Should().Be("INTERPRETING");
        body.RootElement.GetProperty("text_input").GetString().Should().Be("создай задачу");
        Fixture.Scheduler.Scheduled.Should().Contain((JobKind.Nlu, turnId));
    }

    [Fact]
    public async Task Health_reports_database_and_scheduler()
    {
        // Act
        HttpResponseMessage response = await Client.GetAsync("/api/v1/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("ok");
        body.RootElement.GetProperty("db").GetString().Should().Be("ok");
        body.RootElement.GetProperty("scheduler").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Errors_use_common_error_json_shape()
    {
        // Act
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/turns/{Guid.NewGuid()}");

        // Assert
        await AssertErrorAsync(response, HttpStatusCode.NotFound, "TURN_NOT_FOUND");
    }

    [Fact]
    public async Task Unexpected_failure_returns_internal_error_json()
    {
        // Arrange
        Fixture.Storage.FailPresign = true;

        // Act
        HttpResponseMessage response = await PostJsonAsync(
            "/api/v1/uploads",
            new { content_type = "audio/webm", size_bytes = 42_000 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using JsonDocument body = await ReadJsonAsync(response);
        JsonElement error = body.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("INTERNAL");
        error.GetProperty("retryable").GetBoolean().Should().BeTrue();
    }

    private async Task<int> DraftCountAsync() =>
        await WithScopeAsync(provider =>
        {
            var dbContext = provider.GetRequiredService<TaskManagerDbContext>();
            return dbContext.Drafts.CountAsync();
        });

    private async Task<TurnState> TurnStateAsync(Guid turnId) =>
        await WithScopeAsync(provider =>
        {
            var dbContext = provider.GetRequiredService<TaskManagerDbContext>();
            return dbContext.Turns
                .Where(turn => turn.Id == turnId)
                .Select(turn => turn.State)
                .SingleAsync();
        });

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        response.StatusCode.Should().Be(status);

        string content = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(content);
        JsonElement error = body.RootElement.GetProperty("error");

        error.GetProperty("code").GetString().Should().Be(code);
        error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        error.GetProperty("retryable").GetBoolean().Should().BeFalse();
    }
}
