using TaskManager.Api.Contracts;
using TaskManager.Application.Abstractions;
using TaskManager.Infrastructure.Persistence;

namespace TaskManager.Api.Endpoints;

/// <summary>Живость процесса, БД и планировщика.</summary>
public static class HealthEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/health", Get);
    }

    private static async Task<IResult> Get(
        TaskManagerDbContext dbContext,
        IJobScheduler scheduler,
        CancellationToken cancellationToken)
    {
        bool dbOk = await CanConnectAsync(dbContext, cancellationToken);
        bool schedulerOk = await SchedulerRespondsAsync(scheduler, cancellationToken);
        bool ok = dbOk && schedulerOk;

        return ApiResults.Json(
            new HealthResponse(
                ok ? "ok" : "degraded",
                dbOk ? "ok" : "down",
                schedulerOk ? "ok" : "down"),
            ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<bool> CanConnectAsync(
        TaskManagerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> SchedulerRespondsAsync(
        IJobScheduler scheduler,
        CancellationToken cancellationToken)
    {
        try
        {
            // Проверка чтением: не создаёт задачу и работает одинаково для Quartz и fake.
            await scheduler.ExistsAsync(JobKind.Stt, Guid.NewGuid(), cancellationToken);
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return false;
        }
    }
}
