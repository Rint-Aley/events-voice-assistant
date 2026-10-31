using Quartz;
using TaskManager.Application.Abstractions;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Обёртка над Quartz для Application. Ключ задачи детерминирован, поэтому повторная
/// постановка не создаёт вторую задачу. Ретраи выполняет сам Quartz по
/// <see cref="Quartz.RetryPolicy"/> триггера; своего цикла и <c>Thread.Sleep</c> здесь нет.
/// </summary>
public sealed class QuartzJobScheduler : IJobScheduler
{
    private readonly IScheduler _scheduler;
    private readonly QuartzJobSchedulerOptions _options;

    public QuartzJobScheduler(IScheduler scheduler, QuartzJobSchedulerOptions? options = null)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _options = options ?? new QuartzJobSchedulerOptions();
        _options.Validate();
    }

    public async Task ScheduleAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(targetId);

        TriggerKey triggerKey = JobKeys.Trigger(kind, targetId);

        if (await _scheduler.Exists(triggerKey, cancellationToken))
        {
            return;
        }

        JobKey jobKey = JobKeys.Job(kind, targetId);

        IJobDetail job = JobBuilder.Create()
            .OfType(JobKeys.JobType(kind))
            .WithIdentity(jobKey)
            // После падения процесса задача возвращается в работу, а не остаётся
            // в BLOCKED/ERROR: планировщик не должен терять незавершённый ход.
            .RequestRecovery()
            .UsingJobData(JobData.TargetId, targetId)
            .UsingJobData(JobData.Kind, kind.ToString())
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(job)
            .StartNow()
            .WithRetryPolicy(_options.CreateRetryPolicy())
            .Build();

        try
        {
            await _scheduler.ScheduleJob(job, trigger, cancellationToken: cancellationToken);
        }
        catch (ObjectAlreadyExistsException)
        {
            // Между Exists и ScheduleJob задачу успел поставить reconciler или другой тик.
        }
    }

    public Task<bool> ExistsAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(targetId);

        return _scheduler.Exists(JobKeys.Trigger(kind, targetId), cancellationToken)
            .AsTask();
    }

    public async Task RemoveAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(targetId);

        await _scheduler.UnscheduleJob(JobKeys.Trigger(kind, targetId), cancellationToken);
        await _scheduler.DeleteJob(JobKeys.Job(kind, targetId), cancellationToken);
    }

    private static void ValidateTarget(Guid targetId)
    {
        if (targetId == Guid.Empty)
        {
            throw new ArgumentException("Job target identifier is required.", nameof(targetId));
        }
    }
}
