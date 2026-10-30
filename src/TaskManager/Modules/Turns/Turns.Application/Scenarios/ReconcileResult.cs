namespace TaskManager.Turns.Application.Scenarios;

/// <summary>Что сделал один проход <c>reconciler</c>.</summary>
public sealed record ReconcileResult(
    int ExpiredTurns,
    int ScheduledJobs,
    int FailedAudio,
    int ApplyFailedTurns);
