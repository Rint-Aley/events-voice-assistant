using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Регистрирует периодические задачи <c>apply</c> и <c>reconciler</c>. Идентификаторы
/// фиксированы, поэтому повторный старт обновляет те же триггеры, а не создаёт вторые.
/// </summary>
public sealed class RecurringJobHostedService : IHostedService
{
    private readonly IScheduler _scheduler;
    private readonly QuartzJobSchedulerOptions _options;
    private readonly ILogger<RecurringJobHostedService> _logger;

    public RecurringJobHostedService(
        IScheduler scheduler,
        QuartzJobSchedulerOptions options,
        ILogger<RecurringJobHostedService> logger)
    {
        _scheduler = scheduler;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureAsync<ApplyJob>(
            JobKeys.ApplyJob,
            JobKeys.ApplyTrigger,
            _options.ApplyTickInterval,
            cancellationToken);

        await EnsureAsync<ReconcilerJob>(
            JobKeys.ReconcilerJob,
            JobKeys.ReconcilerTrigger,
            _options.ReconcileInterval,
            cancellationToken);

        _logger.LogInformation("Recurring TaskManager jobs are registered.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureAsync<TJob>(
        JobKey jobKey,
        TriggerKey triggerKey,
        TimeSpan interval,
        CancellationToken cancellationToken)
        where TJob : IJob
    {
        IJobDetail job = JobBuilder.Create<TJob>()
            .WithIdentity(jobKey)
            .StoreDurably()
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(schedule => schedule
                .WithInterval(interval)
                .RepeatForever())
            .WithRetryPolicy(RetryPolicy.None)
            .Build();

        // Replace=true делает старт идемпотентным для персистентного store:
        // тот же job и trigger перезаписываются, второй экземпляр не появляется.
        await _scheduler.ScheduleJob(
            job,
            trigger,
            new ScheduleJobOptions { Replace = true },
            cancellationToken);
    }
}
