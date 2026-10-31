using Microsoft.AspNetCore.Http.Features;
using TaskManager.Api.Configuration;
using TaskManager.Api.Contracts;
using TaskManager.Api.Sse;
using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Queries;
using TaskManager.Turns.Application.Scenarios;
using TaskManager.Turns.Domain;

namespace TaskManager.Api.Endpoints;

/// <summary>
/// Снапшот хода, отмена черновика и SSE. Поток строит Api: сценарий только читает
/// актуальное состояние, поэтому переподключение всегда получает снапшот.
/// </summary>
public sealed class TurnsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/turns/{id}", Get);
        api.MapDelete("/turns/{id}/draft", CancelDraft);
        api.MapGet("/turns/{id}/events", StreamEvents);
    }

    private static async Task<IResult> Get(
        string id,
        GetTurnSnapshotHandler snapshots,
        IObjectStorage storage,
        ILogger<TurnsEndpoints> logger,
        CancellationToken cancellationToken)
    {
        if (!TryParseTurnId(id, out Guid turnId))
        {
            return ApiResults.TurnNotFound();
        }

        Result<TurnSnapshot> result = await snapshots.HandleAsync(turnId, cancellationToken);

        if (result.IsFailure)
        {
            return ApiResults.FromDomainFailure(result.Error!);
        }

        TurnSnapshot snapshot = result.Value;
        AudioResponse? audio = await GetAudioAsync(snapshot, storage, logger, cancellationToken);

        return ApiResults.Json(
            ToResponse(snapshot, audio),
            StatusCodes.Status200OK);
    }

    private static async Task<IResult> CancelDraft(
        string id,
        CancelDraftHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryParseTurnId(id, out Guid turnId))
        {
            return ApiResults.TurnNotFound();
        }

        Result<CancelDraftResult> result = await handler.HandleAsync(turnId, cancellationToken);

        if (result.IsFailure)
        {
            return ApiResults.FromDomainFailure(result.Error!);
        }

        return ApiResults.Json(
            new DraftCancelledResponse(result.Value.Text),
            StatusCodes.Status200OK);
    }

    private static async Task StreamEvents(
        HttpContext context,
        string id,
        GetTurnSnapshotHandler snapshots,
        IObjectStorage storage,
        TaskManagerOptions options,
        TimeProvider time,
        ILogger<TurnsEndpoints> logger)
    {
        CancellationToken cancellationToken = context.RequestAborted;

        if (!TryParseTurnId(id, out Guid turnId))
        {
            await ApiResults.WriteErrorAsync(
                context.Response,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.TurnNotFound,
                "Ход не найден",
                cancellationToken: cancellationToken);
            return;
        }

        Result<TurnSnapshot> initial = await snapshots.HandleAsync(turnId, cancellationToken);

        if (initial.IsFailure)
        {
            await ApiResults.WriteErrorAsync(
                context.Response,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.TurnNotFound,
                "Ход не найден",
                cancellationToken: cancellationToken);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await using var writer = new SseWriter(context.Response.Body);

        var stream = new StreamProgress();
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset nextPing = now + options.SsePingInterval;

        await EmitSnapshotAsync(
            writer,
            initial.Value,
            storage,
            logger,
            cancellationToken,
            stream);

        if (stream.AnswerSent)
        {
            stream.AnswerAt = now;
        }

        if (stream.AudioSent || stream.ErrorSent || stream.AudioUnavailable)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            now = time.GetUtcNow();

            if (now >= nextPing)
            {
                await writer.WriteCommentAsync("ping", cancellationToken);
                nextPing = now + options.SsePingInterval;
            }

            Result<TurnSnapshot> refreshed = await snapshots.HandleAsync(turnId, cancellationToken);
            if (refreshed.IsFailure)
            {
                // Ход не удаляется в MVP; если данные пропали, поток продолжать нечем.
                return;
            }

            await EmitSnapshotAsync(
                writer,
                refreshed.Value,
                storage,
                logger,
                cancellationToken,
                stream);

            if (stream.AnswerSent && stream.AnswerAt is null)
            {
                stream.AnswerAt = now;
            }

            if (stream.AudioSent || stream.ErrorSent || stream.AudioUnavailable)
            {
                return;
            }

            if (stream.AnswerSent
                && stream.AnswerAt is { } answerAt
                && now - answerAt >= options.SseAudioWait)
            {
                return;
            }

            if (!stream.AnswerSent && TurnStateMachine.IsTerminal(refreshed.Value.State))
            {
                return;
            }

            await Task.Delay(options.SsePollInterval, time, cancellationToken);
        }
    }

    private static async Task EmitSnapshotAsync(
        SseWriter writer,
        TurnSnapshot snapshot,
        IObjectStorage storage,
        ILogger logger,
        CancellationToken cancellationToken,
        StreamProgress stream)
    {
        string state = snapshot.State.ToContract();

        if (stream.LastState != state)
        {
            await writer.WriteEventAsync(
                "state",
                new StateStreamData(state),
                cancellationToken);
            stream.LastState = state;
        }

        if (!stream.AnswerSent && snapshot.Answer is { } answer)
        {
            await writer.WriteEventAsync(
                "answer",
                answer.ToAnswerData(),
                cancellationToken);
            stream.AnswerSent = true;
        }

        if (stream.AnswerSent && !stream.AudioSent)
        {
            AudioResponse? audio = await GetAudioAsync(snapshot, storage, logger, cancellationToken);
            if (audio is not null)
            {
                await writer.WriteEventAsync(
                    "audio",
                    new AudioStreamData(audio.Url, audio.DurationMs),
                    cancellationToken);
                stream.AudioSent = true;
            }
            else if (AudioWillNotArrive(snapshot))
            {
                // Озвучка провалилась или не требовалась: ждать SSE_AUDIO_WAIT_SECONDS нечего.
                stream.AudioUnavailable = true;
            }
        }

        // answer и error взаимоисключающие: ошибка отправляется только когда ответа нет.
        if (!stream.AnswerSent && !stream.ErrorSent && snapshot.ErrorCode is { } errorCode)
        {
            TurnErrorResponse error = errorCode.ToError();
            await writer.WriteEventAsync(
                "error",
                new ErrorStreamData(error.Code, error.Message),
                cancellationToken);
            stream.ErrorSent = true;
        }
    }

    private sealed class StreamProgress
    {
        public string? LastState { get; set; }

        public bool AnswerSent { get; set; }

        public bool AudioSent { get; set; }

        public bool AudioUnavailable { get; set; }

        public bool ErrorSent { get; set; }

        public DateTimeOffset? AnswerAt { get; set; }
    }

    private static bool AudioWillNotArrive(TurnSnapshot snapshot) =>
        snapshot.Answer is
        {
            AudioStatus: AnswerAudioStatus.Failed or AnswerAudioStatus.Skipped,
        };

    private static TurnResponse ToResponse(TurnSnapshot snapshot, AudioResponse? audio)
    {
        TurnSnapshotAnswer? answer = snapshot.Answer;

        return new TurnResponse(
            snapshot.TurnId,
            snapshot.State.ToContract(),
            snapshot.Text,
            answer?.Text,
            answer?.Events.Select(item => item.ToContract()).ToArray() ?? [],
            answer?.TotalCount,
            answer?.Warnings ?? [],
            audio,
            answer is null && snapshot.ErrorCode is { } code ? code.ToError() : null);
    }

    private static async Task<AudioResponse?> GetAudioAsync(
        TurnSnapshot snapshot,
        IObjectStorage storage,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (snapshot.Answer is not
            {
                AudioStatus: AnswerAudioStatus.Ready,
                AudioObjectKey: { } objectKey,
            } answer)
        {
            return null;
        }

        try
        {
            Uri url = await storage.CreatePresignedDownloadUrlAsync(
                objectKey,
                cancellationToken);

            return new AudioResponse(url.ToString(), answer.AudioDurationMs);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Текст ответа важнее озвучки: недоступный S3 не должен ломать снапшот.
            logger.LogWarning(
                error,
                "Cannot create presigned URL for answer audio {ObjectKey}.",
                objectKey);
            return null;
        }
    }

    private static bool TryParseTurnId(string value, out Guid turnId) =>
        Guid.TryParse(value, out turnId) && turnId != Guid.Empty;
}
