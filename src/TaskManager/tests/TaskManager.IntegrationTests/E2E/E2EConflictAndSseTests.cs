using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Infrastructure.Persistence;
using TaskManager.IntegrationTests.Api;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Clients.Tts;

namespace TaskManager.IntegrationTests.E2E;

/// <summary>
/// Сквозные сценарии конфликтов встреч и SSE: предупреждения за период
/// <c>[start, start + duration)</c>, текст раньше аудио.
/// </summary>
public sealed class E2EConflictAndSseTests(ApiTestFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Overlapping_meeting_is_saved_with_all_warnings_and_first_conflict_in_text()
    {
        // Arrange
        await CreateMeetingAsync("созвон", "2026-10-31", "15:00");

        // Act
        Guid overlappingId = await CreateMeetingAsync("планёрка", "2026-10-31", "15:30");
        JsonElement snapshot = await GetSnapshotAsync(overlappingId);

        // Assert
        snapshot.GetProperty("state").GetString().Should().Be("COMPLETED");

        JsonElement warnings = snapshot.GetProperty("warnings");
        warnings.GetArrayLength().Should().Be(1);
        warnings[0].GetString().Should().Be(
            "На это время уже запланирована встреча «созвон». Выбери другое время.");

        snapshot.GetProperty("answer").GetString().Should().Be(
            "Записала встречу: «планёрка» — завтра, 15:30. "
            + "На это время уже запланирована встреча «созвон». Выбери другое время.");

        snapshot.GetProperty("events").GetArrayLength().Should().Be(1);
        snapshot.GetProperty("events")[0].GetProperty("description").GetString()
            .Should().Be("планёрка");

        int storedMeetings = await WithScopeAsync(provider =>
            provider.GetRequiredService<TaskManagerDbContext>().Events
                .CountAsync(@event => @event.Description == "созвон"
                    || @event.Description == "планёрка"));
        storedMeetings.Should().Be(2);
    }

    [Fact]
    public async Task Meeting_starting_at_previous_end_is_not_a_conflict()
    {
        // Arrange
        await CreateMeetingAsync("первая", "2026-10-31", "15:00");

        // Act: default meeting duration — 60 минут, поэтому 16:00 уже не пересекается.
        Guid adjacentId = await CreateMeetingAsync("соседняя", "2026-10-31", "16:00");
        JsonElement snapshot = await GetSnapshotAsync(adjacentId);

        // Assert
        snapshot.GetProperty("warnings").GetArrayLength().Should().Be(0);
        snapshot.GetProperty("answer").GetString()
            .Should().Be("Записала встречу: «соседняя» — завтра, 16:00.");
    }

    [Fact]
    public async Task Sse_sends_text_immediately_and_audio_after_synthesis()
    {
        // Arrange
        Guid turnId = await CreateTextTurnAsync("купи хлеб");
        Fixture.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "create_task",
            0.9,
            new NluEntities(Description: "купить хлеб"),
            Embedding: Embedding());
        await RunNluJobAsync(turnId);
        await RunApplyJobAsync(turnId);

        // Act: до синтеза приходят только state и answer
        using HttpResponseMessage response = await OpenStreamAsync(turnId);
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        List<SseFrame> snapshot = await SseReader.ReadAsync(reader, 2, timeout.Token);
        Guid answerId = await GetAnswerIdAsync(turnId);
        Fixture.Tts.Respond = request => new TtsResult(
            request.AnswerId,
            $"tts/{request.AnswerId}.ogg",
            "audio/ogg",
            "ogg_opus",
            4200,
            Cached: false);

        await RunTtsJobAsync(answerId);
        SseFrame? audio = await SseReader.ReadUntilAsync(
            reader,
            frame => frame.Event == "audio",
            timeout.Token);

        // Assert
        snapshot.Select(frame => frame.Event).Should().Equal("state", "answer");
        snapshot.Should().NotContain(frame => frame.Event == "error");

        using JsonDocument answer = JsonDocument.Parse(snapshot[1].Data!);
        answer.RootElement.GetProperty("text").GetString()
            .Should().Be("Записала задачу: «купить хлеб».");
        answer.RootElement.GetProperty("events").GetArrayLength().Should().Be(1);

        audio.Should().NotBeNull();
        using JsonDocument audioBody = JsonDocument.Parse(audio!.Data!);
        audioBody.RootElement.GetProperty("url").GetString().Should().Contain("tts/");
        audioBody.RootElement.GetProperty("duration_ms").GetInt32().Should().Be(4200);

        JsonElement final = await GetSnapshotAsync(turnId);
        final.GetProperty("audio").GetProperty("duration_ms").GetInt32().Should().Be(4200);
    }

    private Task<Guid> CreateMeetingAsync(string description, string startsOn, string startsAt) =>
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
