using TaskManager.Api.Configuration;
using TaskManager.Api.Contracts;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.Api.Endpoints;

/// <summary>
/// Создание хода. Ответ <c>202</c> отдаётся до обработки: сценарий только фиксирует ход
/// и ставит фоновую задачу. Валидация формы запроса — граница HTTP, не Domain.
/// </summary>
public static class MessagesEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/messages", Create);
    }

    private static async Task<IResult> Create(
        HttpRequest request,
        SubmitTextHandler submitText,
        SubmitVoiceHandler submitVoice,
        TaskManagerOptions options,
        CancellationToken cancellationToken)
    {
        JsonReadResult<MessageRequest> read = await ApiResults.ReadJsonAsync<MessageRequest>(
            request,
            cancellationToken);

        if (read.Error is not null)
        {
            return read.Error;
        }

        MessageRequest body = read.Value!;

        bool hasText = body.Text is not null;
        bool hasObjectKey = body.ObjectKey is not null;

        if (hasText == hasObjectKey)
        {
            return ApiResults.BadRequest("Нужно ровно одно из text и object_key");
        }

        if (hasText && string.IsNullOrWhiteSpace(body.Text))
        {
            return ApiResults.BadRequest("Текст не может быть пустым");
        }

        if (hasObjectKey && string.IsNullOrWhiteSpace(body.ObjectKey))
        {
            return ApiResults.BadRequest("object_key не может быть пустым");
        }

        if (hasText && body.Text!.Length > ApiLimits.MaxTextLength)
        {
            return ApiResults.Error(
                StatusCodes.Status413PayloadTooLarge,
                ApiErrorCodes.PayloadTooLarge,
                $"Текст длиннее {ApiLimits.MaxTextLength} символов");
        }

        if (string.IsNullOrWhiteSpace(body.IdempotencyKey)
            || !Guid.TryParse(body.IdempotencyKey, out Guid idempotencyKey)
            || idempotencyKey == Guid.Empty)
        {
            return ApiResults.BadRequest("idempotency_key обязателен и должен быть uuid");
        }

        Result<SubmitTurnResult> result = hasText
            ? await submitText.HandleAsync(
                new SubmitTextCommand(options.SessionId, idempotencyKey, body.Text!),
                cancellationToken)
            : await submitVoice.HandleAsync(
                new SubmitVoiceCommand(options.SessionId, idempotencyKey, body.ObjectKey!),
                cancellationToken);

        if (result.IsFailure)
        {
            return ApiResults.FromDomainFailure(result.Error!);
        }

        SubmitTurnResult turn = result.Value;

        return ApiResults.Json(
            new MessageResponse(
                turn.TurnId,
                turn.State.ToContract(),
                $"/api/v1/turns/{turn.TurnId}/events"),
            StatusCodes.Status202Accepted);
    }
}
