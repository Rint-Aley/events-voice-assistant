using TaskManager.Api.Contracts;
using TaskManager.Api.Endpoints;

namespace TaskManager.Api.Infrastructure;

/// <summary>
/// Последний рубеж: непредвиденные ошибки не должны отдавать HTML или трассировку.
/// Если SSE уже начал ответ, соединение обрывается — формат уже нельзя изменить.
/// </summary>
public sealed class ApiExceptionMiddleware(
    RequestDelegate next,
    ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Клиент закрыл SSE или отменил запрос.
        }
        catch (Exception error)
        {
            logger.LogError(error, "Unhandled API exception for {Method} {Path}.",
                context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                context.Abort();
                return;
            }

            context.Response.Clear();
            await ApiResults.WriteErrorAsync(
                context.Response,
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.Internal,
                "Внутренняя ошибка",
                retryable: true,
                context.RequestAborted);
        }
    }
}
