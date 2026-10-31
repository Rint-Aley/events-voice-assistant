using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Application.Abstractions;
using TaskManager.Events.Domain;
using TaskManager.IntegrationTests.Api;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;

namespace TaskManager.IntegrationTests.E2E;

/// <summary>
/// Сквозная обвязка: HTTP-запрос создаёт ход, fake-планировщик фиксирует задачи, а тест
/// исполняет их теми же Application-сценариями, которые вызывают Quartz-джобы. Реальный
/// Postgres и Api остаются в цепочке; STT/NLU/TTS/S3/Quartz заменены заглушками.
/// </summary>
public abstract class E2ETestBase : ApiTestBase
{
    protected E2ETestBase(ApiTestFixture fixture)
        : base(fixture)
    {
    }

    protected static NluEmbedding Embedding() =>
        new(
            EventEmbedding.Model,
            EventEmbedding.Dimensions,
            Enumerable.Repeat(0.1, EventEmbedding.Dimensions).ToArray());

    protected async Task<Result<InterpretTurnResult>> RunNluJobAsync(Guid turnId)
    {
        Result<InterpretTurnResult> result = await WithScopeAsync(provider =>
            provider.GetRequiredService<InterpretTurnHandler>().HandleAsync(turnId));

        // В реальном Quartz сработавший trigger удаляется; fake-планировщик этого не делает.
        await Fixture.Scheduler.RemoveAsync(JobKind.Nlu, turnId);

        return result;
    }

    protected async Task<Result<ApplyTurnPlanResult>> RunApplyJobAsync(Guid turnId)
    {
        Result<ApplyTurnPlanResult> result = await WithScopeAsync(provider =>
            provider.GetRequiredService<ApplyTurnPlanHandler>().HandleAsync(turnId));

        return result;
    }

    protected async Task<Result<SynthesizeAnswerResult>> RunTtsJobAsync(Guid answerId)
    {
        Result<SynthesizeAnswerResult> result = await WithScopeAsync(provider =>
            provider.GetRequiredService<SynthesizeAnswerHandler>().HandleAsync(answerId));

        await Fixture.Scheduler.RemoveAsync(JobKind.Tts, answerId);

        return result;
    }

    protected async Task<Result<SubmitTurnResult>> RunSttJobAsync(Guid turnId)
    {
        Result<SubmitTurnResult> result = await WithScopeAsync(provider =>
            provider.GetRequiredService<TranscribeTurnHandler>().HandleAsync(turnId));

        await Fixture.Scheduler.RemoveAsync(JobKind.Stt, turnId);

        return result;
    }

    /// <summary>
    /// Полный путь записи: HTTP → NLU → APPLYING → применение команд → COMPLETED.
    /// Возвращает идентификатор завершённого хода.
    /// </summary>
    protected async Task<Guid> CreateEventThroughPipelineAsync(
        string text,
        NluResponse response)
    {
        Guid turnId = await CreateTextTurnAsync(text);
        Fixture.Nlu.Respond = _ => response;

        Result<InterpretTurnResult> interpreted = await RunNluJobAsync(turnId);
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.State.Should().Be(TurnState.Applying);

        Result<ApplyTurnPlanResult> applied = await RunApplyJobAsync(turnId);
        applied.IsSuccess.Should().BeTrue();
        applied.Value.State.Should().Be(TurnState.Completed);

        return turnId;
    }

    /// <summary>Полный путь чтения: HTTP → NLU → COMPLETED без команд записи.</summary>
    protected async Task<Guid> RunListTurnAsync(
        string text,
        string from,
        string to,
        string? typeFilter = null)
    {
        Guid turnId = await CreateTextTurnAsync(text);
        Fixture.Nlu.Respond = _ => new NluResponse(
            Guid.Empty,
            "list_period",
            0.9,
            new NluEntities(
                PeriodFrom: from,
                PeriodTo: to,
                TypeFilter: typeFilter));

        Result<InterpretTurnResult> interpreted = await RunNluJobAsync(turnId);
        interpreted.IsSuccess.Should().BeTrue();
        interpreted.Value.State.Should().Be(TurnState.Completed);

        return turnId;
    }

    protected async Task<JsonElement> GetSnapshotAsync(Guid turnId)
    {
        HttpResponseMessage response = await Client.GetAsync($"/api/v1/turns/{turnId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using JsonDocument body = await ReadJsonAsync(response);

        return body.RootElement.Clone();
    }

    protected async Task<HttpResponseMessage> OpenStreamAsync(Guid turnId)
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

    protected static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        bool retryable = false)
    {
        response.StatusCode.Should().Be(status);

        string content = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(content);
        JsonElement error = body.RootElement.GetProperty("error");

        error.GetProperty("code").GetString().Should().Be(code);
        error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        error.GetProperty("retryable").GetBoolean().Should().Be(retryable);
    }
}
