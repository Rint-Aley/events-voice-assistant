using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Clients.Tts;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;

namespace TaskManager.IntegrationTests.Api;

public sealed class ApiSseTests(ApiTestFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Sse_snapshot_sends_state_then_answer_then_audio()
    {
        // Arrange
        Guid turnId = await CreateCompletedTurnWithAudioAsync();

        // Act
        using HttpResponseMessage response = await OpenStreamAsync(turnId);
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        List<SseFrame> frames = await SseReader.ReadAsync(reader, 3, timeout.Token);

        // Assert
        frames.Select(frame => frame.Event).Should().Equal("state", "answer", "audio");

        using JsonDocument state = JsonDocument.Parse(frames[0].Data!);
        state.RootElement.GetProperty("state").GetString().Should().Be("COMPLETED");

        using JsonDocument answer = JsonDocument.Parse(frames[1].Data!);
        answer.RootElement.GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace();
        answer.RootElement.GetProperty("events").GetArrayLength().Should().Be(0);

        using JsonDocument audio = JsonDocument.Parse(frames[2].Data!);
        audio.RootElement.GetProperty("url").GetString().Should().Contain("tts/");
        audio.RootElement.GetProperty("duration_ms").GetInt32().Should().Be(6100);

        frames.Should().NotContain(frame => frame.Event == "error");
    }

    [Fact]
    public async Task Sse_sends_answer_before_audio_and_audio_can_arrive_later()
    {
        // Arrange
        Guid turnId = await CreateCompletedTurnAsync();

        // Act
        using HttpResponseMessage response = await OpenStreamAsync(turnId);
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        List<SseFrame> snapshot = await SseReader.ReadAsync(reader, 2, timeout.Token);
        Guid answerId = await GetAnswerIdAsync(turnId);
        await SynthesizeAsync(answerId);
        SseFrame? audio = await SseReader.ReadUntilAsync(
            reader,
            frame => frame.Event == "audio",
            timeout.Token);

        // Assert
        snapshot.Select(frame => frame.Event).Should().Equal("state", "answer");
        snapshot.Should().NotContain(frame => frame.Event == "error");

        audio.Should().NotBeNull();
        using JsonDocument audioBody = JsonDocument.Parse(audio!.Data!);
        audioBody.RootElement.GetProperty("url").GetString().Should().Contain("tts/");
        audioBody.RootElement.GetProperty("duration_ms").GetInt32().Should().Be(6100);
    }

    [Fact]
    public async Task Sse_closes_after_answer_when_audio_failed()
    {
        // Arrange
        Guid turnId = await CreateCompletedTurnAsync();
        Guid answerId = await GetAnswerIdAsync(turnId);
        await WithScopeAsync(async provider =>
        {
            var markFailed = provider.GetRequiredService<MarkAnswerAudioFailedHandler>();
            var result = await markFailed.HandleAsync(answerId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Status.Should().Be(AnswerAudioStatus.Failed);
        });

        // Act
        using HttpResponseMessage response = await OpenStreamAsync(turnId);
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        List<SseFrame> frames = await SseReader.ReadAsync(reader, 3, timeout.Token);

        // Assert
        // Аудио уже не появится: поток закрывается сразу после answer, а не висит
        // SSE_AUDIO_WAIT_SECONDS (в тестах — 60 с) и не шлёт ping.
        frames.Select(frame => frame.Event).Should().Equal("state", "answer");
        frames.Should().NotContain(frame => frame.Event == "audio");
    }

    [Fact]
    public async Task Sse_failed_turn_sends_error_and_not_answer()
    {
        // Arrange
        Guid turnId = await CreateTextTurnAsync("сломанный запрос");
        await WithScopeAsync(async provider =>
        {
            var fail = provider.GetRequiredService<FailTurnHandler>();
            var result = await fail.HandleAsync(turnId, TurnTransition.NluFailed);
            result.IsSuccess.Should().BeTrue();
        });

        // Act
        using HttpResponseMessage response = await OpenStreamAsync(turnId);
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        List<SseFrame> frames = await SseReader.ReadAsync(reader, 2, timeout.Token);

        // Assert
        frames.Select(frame => frame.Event).Should().Equal("state", "error");
        frames.Should().NotContain(frame => frame.Event == "answer");

        using JsonDocument state = JsonDocument.Parse(frames[0].Data!);
        state.RootElement.GetProperty("state").GetString().Should().Be("FAILED");

        using JsonDocument error = JsonDocument.Parse(frames[1].Data!);
        error.RootElement.GetProperty("code").GetString().Should().Be("NLU_FAILED");
        error.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Sse_keeps_connection_with_ping_while_turn_is_active()
    {
        // Arrange
        Guid turnId = await CreateTextTurnAsync("долгий запрос");

        // Act
        using HttpResponseMessage response = await OpenStreamAsync(turnId);
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        SseFrame? ping = await SseReader.ReadUntilAsync(
            reader,
            frame => frame.Comment == "ping",
            timeout.Token);

        // Assert
        ping.Should().NotBeNull();
        ping!.Comment.Should().Be("ping");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Sse_unknown_turn_returns_404_json(string turnId)
    {
        // Act
        using HttpResponseMessage response = await Client.GetAsync($"/api/v1/turns/{turnId}/events");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using JsonDocument body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("TURN_NOT_FOUND");
    }

    private async Task<HttpResponseMessage> OpenStreamAsync(Guid turnId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/turns/{turnId}/events");
        request.Headers.Accept.ParseAdd("text/event-stream");

        HttpResponseMessage response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        return response;
    }

    private async Task<Guid> CreateCompletedTurnAsync()
    {
        Guid turnId = await CreateTextTurnAsync("что у меня на неделе");
        ConfigureListPeriodNlu();

        await WithScopeAsync(async provider =>
        {
            var interpret = provider.GetRequiredService<InterpretTurnHandler>();
            var result = await interpret.HandleAsync(turnId);
            result.IsSuccess.Should().BeTrue();
            result.Value.State.Should().Be(TurnState.Completed);
        });

        return turnId;
    }

    private async Task<Guid> CreateCompletedTurnWithAudioAsync()
    {
        Guid turnId = await CreateCompletedTurnAsync();
        Guid answerId = await GetAnswerIdAsync(turnId);
        await SynthesizeAsync(answerId);

        return turnId;
    }

    private void ConfigureListPeriodNlu()
    {
        Fixture.Nlu.Respond = request => new NluResponse(
            request.TurnId,
            "list_period",
            0.9,
            new NluEntities(PeriodFrom: "2026-11-01", PeriodTo: "2026-11-30"));
    }

    private async Task SynthesizeAsync(Guid answerId)
    {
        Fixture.Tts.Respond = request => new TtsResult(
            request.AnswerId,
            $"tts/{request.AnswerId}.ogg",
            "audio/ogg",
            "ogg_opus",
            6100,
            Cached: false);

        await WithScopeAsync(async provider =>
        {
            var synthesize = provider.GetRequiredService<SynthesizeAnswerHandler>();
            var result = await synthesize.HandleAsync(answerId);
            result.IsSuccess.Should().BeTrue();
            result.Value.AudioStatus.Should().Be(AnswerAudioStatus.Ready);
        });
    }
}
