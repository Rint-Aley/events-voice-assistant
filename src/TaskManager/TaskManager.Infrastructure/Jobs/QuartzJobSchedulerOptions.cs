using Quartz;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Настройки фоновых задач. <see cref="MaxAttempts"/> — общее число выполнений,
/// включая первое; Quartz хранит число повторов после первого падения.
/// </summary>
public sealed record QuartzJobSchedulerOptions
{
    public int MaxAttempts { get; init; } = 3;

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ApplyTickInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReconcileInterval { get; init; } = TimeSpan.FromSeconds(10);

    internal RetryPolicy CreateRetryPolicy()
    {
        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxAttempts),
                "At least one attempt is required.");
        }

        if (RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryDelay),
                "Retry delay cannot be negative.");
        }

        return MaxAttempts == 1
            ? RetryPolicy.None
            : RetryPolicy.Fixed(MaxAttempts - 1, RetryDelay);
    }

    internal void Validate()
    {
        _ = CreateRetryPolicy();

        if (ApplyTickInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ApplyTickInterval),
                "Apply tick interval must be positive.");
        }

        if (ReconcileInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReconcileInterval),
                "Reconcile interval must be positive.");
        }
    }
}
