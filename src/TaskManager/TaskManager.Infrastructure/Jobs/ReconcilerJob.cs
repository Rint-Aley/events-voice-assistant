using Microsoft.Extensions.Logging;
using Quartz;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Тик <c>reconciler</c>: сверяет БД с планировщиком. Ошибка прохода не валит процесс —
/// следующий тик повторит сверку.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ReconcilerJob : IJob
{
    private readonly ReconcileHandler _reconcile;
    private readonly ILogger<ReconcilerJob> _logger;

    public ReconcilerJob(
        ReconcileHandler reconcile,
        ILogger<ReconcilerJob> logger)
    {
        _reconcile = reconcile;
        _logger = logger;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ReconcileResult result = await _reconcile.HandleAsync(cancellationToken);

            if (result != new ReconcileResult(0, 0, 0, 0))
            {
                _logger.LogInformation(
                    "Reconciler closed {Expired} expired turns, scheduled {Scheduled} jobs, "
                    + "failed {Audio} audio responses and {ApplyFailed} apply turns.",
                    result.ExpiredTurns,
                    result.ScheduledJobs,
                    result.FailedAudio,
                    result.ApplyFailedTurns);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogError(error, "Reconciler pass failed.");
        }
    }
}
