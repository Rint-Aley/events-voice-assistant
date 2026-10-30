using System.Net;
using System.Net.Http;
using System.Text.Json;
using TaskManager.Infrastructure.Clients;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Domain;

namespace TaskManager.IntegrationTests.Infrastructure;

public sealed class NluClientTests
{
    [Fact]
    public async Task InterpretAsync_posts_contract_and_accepts_old_response_without_optional_fields()
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(3));
        (NluClient client, RecordingHttpMessageHandler handler) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "turn_id": "{{turnId}}",
                  "action": "create_meeting",
                  "confidence": 0.91,
                  "entities": {
                    "description": "обсудить релиз",
                    "starts_on": "2026-11-02",
                    "starts_at": "10:00",
                    "duration_minutes": 60
                  },
                  "embedding": {
                    "model": "e5",
                    "dim": 3,
                    "values": [0.1, 0.2, 0.3]
                  }
                }
                """)));

        // Act
        NluResponse result = await client.InterpretAsync(
            new NluRequest(
                turnId,
                "встреча с командой завтра в 10",
                now,
                "Europe/Moscow",
                new NluDraft(DraftKind.Meeting, "обсудить релиз", "2026-11-02", "10:00", 60)));

        // Assert
        result.TurnId.Should().Be(turnId);
        result.Action.Should().Be("create_meeting");
        result.Confidence.Should().Be(0.91);
        result.Entities!.Description.Should().Be("обсудить релиз");
        result.Entities.TypeFilter.Should().BeNull();
        result.Entities.IsDeadline.Should().BeNull();
        result.Embedding!.Model.Should().Be("e5");
        result.Embedding.Dim.Should().Be(3);
        result.Embedding.Values.Should().Equal(0.1, 0.2, 0.3);

        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("http://nlu.local/v1/interpret"));

        using JsonDocument body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("turn_id").GetString().Should().Be(turnId.ToString());
        body.RootElement.GetProperty("text").GetString().Should().Be("встреча с командой завтра в 10");
        body.RootElement.GetProperty("now").GetDateTimeOffset().Should().Be(now);
        body.RootElement.GetProperty("timezone").GetString().Should().Be("Europe/Moscow");
        body.RootElement.GetProperty("draft").GetProperty("kind").GetString().Should().Be("meeting");
        body.RootElement.GetProperty("draft").GetProperty("duration_minutes").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task InterpretAsync_reads_type_filter_and_is_deadline_when_present()
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        (NluClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "turn_id": "{{turnId}}",
                  "action": "list_period",
                  "confidence": 0.8,
                  "entities": {
                    "period_from": "2026-11-01",
                    "period_to": "2026-11-30",
                    "type_filter": "reminder",
                    "is_deadline": true
                  }
                }
                """)));

        // Act
        NluResponse result = await client.InterpretAsync(CreateRequest(turnId));

        // Assert
        result.Entities!.PeriodFrom.Should().Be("2026-11-01");
        result.Entities.PeriodTo.Should().Be("2026-11-30");
        result.Entities.TypeFilter.Should().Be("reminder");
        result.Entities.IsDeadline.Should().BeTrue();
    }

    [Fact]
    public async Task InterpretAsync_omits_draft_when_there_is_nothing_to_clarify()
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        (NluClient client, RecordingHttpMessageHandler handler) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(MinimalResponse(turnId))));

        // Act
        await client.InterpretAsync(CreateRequest(turnId));

        // Assert
        using JsonDocument body = JsonDocument.Parse(
            handler.Requests.Should().ContainSingle().Subject.Body!);
        body.RootElement.TryGetProperty("draft", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InterpretAsync_sends_only_known_draft_slots()
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        (NluClient client, RecordingHttpMessageHandler handler) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(MinimalResponse(turnId))));

        // Act
        await client.InterpretAsync(
            CreateRequest(turnId, new NluDraft(DraftKind.Reminder, null, null, null, null)));

        // Assert
        using JsonDocument body = JsonDocument.Parse(
            handler.Requests.Should().ContainSingle().Subject.Body!);
        JsonElement draft = body.RootElement.GetProperty("draft");
        draft.GetProperty("kind").GetString().Should().Be("reminder");
        draft.TryGetProperty("description", out _).Should().BeFalse();
        draft.TryGetProperty("starts_on", out _).Should().BeFalse();
        draft.TryGetProperty("starts_at", out _).Should().BeFalse();
        draft.TryGetProperty("duration_minutes", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(422, true, "BAD_REQUEST", false)]
    [InlineData(500, false, "INTERNAL", true)]
    [InlineData(503, false, "PROVIDER_UNAVAILABLE", true)]
    [InlineData(500, true, "INTERNAL", false)]
    public async Task InterpretAsync_maps_http_failure_to_permanent_or_retryable(
        int statusCode,
        bool isPermanent,
        string code,
        bool retryable)
    {
        // Arrange
        string body = JsonSerializer.Serialize(new
        {
            error = new { code, message = "failed", retryable },
        });
        (NluClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(body, statusCode)));

        // Act
        Func<Task> act = () => client.InterpretAsync(CreateRequest());

        // Assert
        NluException error = (await act.Should().ThrowAsync<NluException>()).Which;
        error.StatusCode.Should().Be(statusCode);
        error.IsPermanent.Should().Be(isPermanent);
        error.Message.Should().Contain(code);
    }

    [Fact]
    public async Task InterpretAsync_invalid_response_is_permanent()
    {
        // Arrange
        (NluClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json("{ not-json }")));

        // Act
        Func<Task> act = () => client.InterpretAsync(CreateRequest());

        // Assert
        NluException error = (await act.Should().ThrowAsync<NluException>()).Which;
        error.IsPermanent.Should().BeTrue();
        error.InnerException.Should().BeOfType<JsonException>();
    }

    [Fact]
    public async Task InterpretAsync_response_for_another_turn_is_permanent()
    {
        // Arrange
        (NluClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                MinimalResponse(Guid.NewGuid()))));

        // Act
        Func<Task> act = () => client.InterpretAsync(CreateRequest());

        // Assert
        NluException error = (await act.Should().ThrowAsync<NluException>()).Which;
        error.IsPermanent.Should().BeTrue();
        error.Message.Should().Contain("does not match the contract");
    }

    [Fact]
    public async Task InterpretAsync_embedding_without_values_is_permanent()
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        (NluClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "turn_id": "{{turnId}}",
                  "action": "create_task",
                  "confidence": 0.9,
                  "entities": { "description": "купить хлеб" },
                  "embedding": { "model": "e5", "dim": 3 }
                }
                """)));

        // Act
        Func<Task> act = () => client.InterpretAsync(CreateRequest(turnId));

        // Assert
        NluException error = (await act.Should().ThrowAsync<NluException>()).Which;
        error.IsPermanent.Should().BeTrue();
        error.Message.Should().Contain("does not match the contract");
    }

    [Fact]
    public async Task InterpretAsync_timeout_is_retryable()
    {
        // Arrange
        (NluClient client, _) = CreateClient(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            timeout: TimeSpan.FromMilliseconds(100));

        // Act
        Func<Task> act = () => client.InterpretAsync(CreateRequest());

        // Assert
        NluException error = (await act.Should().ThrowAsync<NluException>()).Which;
        error.StatusCode.Should().BeNull();
        error.IsPermanent.Should().BeFalse();
        error.InnerException.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task InterpretAsync_network_error_is_retryable()
    {
        // Arrange
        (NluClient client, _) = CreateClient(
            (_, _) => throw new HttpRequestException("connection refused"));

        // Act
        Func<Task> act = () => client.InterpretAsync(CreateRequest());

        // Assert
        NluException error = (await act.Should().ThrowAsync<NluException>()).Which;
        error.StatusCode.Should().BeNull();
        error.IsPermanent.Should().BeFalse();
        error.InnerException.Should().BeOfType<HttpRequestException>();
    }

    private static string MinimalResponse(Guid turnId) =>
        $$"""
        {
          "turn_id": "{{turnId}}",
          "action": "create_task",
          "confidence": 0.9,
          "entities": { "description": "купить хлеб" },
          "embedding": { "model": "e5", "dim": 3, "values": [0.1, 0.2, 0.3] }
        }
        """;

    private static NluRequest CreateRequest(Guid? turnId = null, NluDraft? draft = null) =>
        new(
            turnId ?? Guid.NewGuid(),
            "купи хлеб",
            new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero),
            "Europe/Moscow",
            draft);

    private static (NluClient Client, RecordingHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        TimeSpan? timeout = null)
    {
        var handler = new RecordingHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://nlu.local/"),
        };

        return (
            new NluClient(httpClient, new NluClientOptions(timeout ?? TimeSpan.FromSeconds(1))),
            handler);
    }
}
