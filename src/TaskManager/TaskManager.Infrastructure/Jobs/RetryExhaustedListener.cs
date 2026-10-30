using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TaskManager.Application.Abstractions;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Финальная обработка после исчерпания retry policy Quartz. Job-типы не знают, что
/// делать с провалом; перевод в <c>FAILED</c> или <c>audio_status = failed</c> выполняет
/// Application-сценарий в отдельном DI-scope.
/// </summary>
public sealed class RetryExhaustedListener : ITriggerListener
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetryExhaustedListener> _logger;

    public RetryExhaustedListener(
        IServiceScopeFactory scopeFactory,
        ILogger<RetryExhaustedListener> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string Name => nameof(RetryExhaustedListener);

    public async ValueTask TriggerRetriesExhausted(
        ITrigger trigger,
        IJobExecutionContext context,
        JobExecutionException exception,
        CancellationToken cancellationToken = default)
    {
        try
        {
            JobKind kind = JobData.KindOf(context);
            Guid targetId = JobData.TargetIdOf(context);

            using IServiceScope scope = _scopeFactory.CreateScope();
            FinalizeJobFailureHandler finalizer =
                scope.ServiceProvider.GetRequiredService<FinalizeJobFailureHandler>();

            await finalizer.HandleAsync(kind, targetId, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Ошибка слушателя не должна ронять планировщик; ход останется активным,
            // и его добьёт reconciler или следующий проход.
            _logger.LogError(
                error,
                "Failed to finalize exhausted job {JobKey}.",
                context.JobDetail.Key);
        }
    }
}
