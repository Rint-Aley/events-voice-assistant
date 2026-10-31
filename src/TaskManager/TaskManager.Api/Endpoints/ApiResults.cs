using System.Text.Json;
using TaskManager.Api.Contracts;
using TaskManager.SharedKernel;
using TaskManager.Turns.Domain;

namespace TaskManager.Api.Endpoints;

/// <summary>Единые ответы API: JSON в <c>snake_case</c> и ошибки по <c>error.json</c>.</summary>
public static class ApiResults
{
    public static IResult Json<T>(T value, int statusCode) =>
        Results.Json(value, ApiJson.Options, statusCode: statusCode);

    public static IResult Error(
        int statusCode,
        string code,
        string message,
        bool retryable = false) =>
        Json(new ErrorResponse(new ErrorBody(code, message, retryable)), statusCode);

    public static IResult BadRequest(string message = "Некорректный запрос") =>
        Error(StatusCodes.Status400BadRequest, ApiErrorCodes.BadRequest, message);

    public static IResult TurnNotFound() =>
        Error(StatusCodes.Status404NotFound, ApiErrorCodes.TurnNotFound, "Ход не найден");

    public static async Task WriteErrorAsync(
        HttpResponse response,
        int statusCode,
        string code,
        string message,
        bool retryable = false,
        CancellationToken cancellationToken = default)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";

        await response.WriteAsJsonAsync(
            new ErrorResponse(new ErrorBody(code, message, retryable)),
            ApiJson.Options,
            cancellationToken: cancellationToken);
    }

    public static async Task<JsonReadResult<T>> ReadJsonAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken)
        where T : class
    {
        // Читалка JSON сама по себе полагается на Content-Type и на неподходящем
        // бросает InvalidOperationException; это ошибка клиента, а не сбой сервера.
        if (!request.HasJsonContentType())
        {
            return new JsonReadResult<T>(null, BadRequest("Некорректный Content-Type"));
        }

        try
        {
            T? value = await request.ReadFromJsonAsync<T>(
                ApiJson.Options,
                cancellationToken);

            return value is null
                ? new JsonReadResult<T>(null, BadRequest("Тело запроса обязательно"))
                : new JsonReadResult<T>(value, null);
        }
        catch (JsonException)
        {
            return new JsonReadResult<T>(null, BadRequest("Некорректный JSON"));
        }
        catch (NotSupportedException)
        {
            return new JsonReadResult<T>(null, BadRequest("Некорректный Content-Type"));
        }
    }

    public static IResult FromDomainFailure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Code switch
        {
            TurnErrors.SessionBusyCode => Error(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.SessionBusy,
                "Предыдущий ход ещё не завершён"),
            TurnErrors.TurnNotFoundCode => TurnNotFound(),
            _ when error.IsExpected => BadRequest(error.Message),
            _ => Error(
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.Internal,
                "Внутренняя ошибка",
                retryable: true),
        };
    }
}

public readonly record struct JsonReadResult<T>(T? Value, IResult? Error)
    where T : class;

/// <summary>Стабильные HTTP-коды API. Не переименовываются.</summary>
public static class ApiErrorCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string UnsupportedAudio = "UNSUPPORTED_AUDIO";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string SessionBusy = "SESSION_BUSY";
    public const string TurnNotFound = "TURN_NOT_FOUND";
    public const string Internal = "INTERNAL";
}
