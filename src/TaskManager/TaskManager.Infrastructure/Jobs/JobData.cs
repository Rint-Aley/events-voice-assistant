using Quartz;
using TaskManager.Application.Abstractions;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>Ключи данных, которые переносятся из постановки в job.</summary>
internal static class JobData
{
    public const string TargetId = "target_id";
    public const string Kind = "kind";

    public static Guid TargetIdOf(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        object? value = context.MergedJobDataMap[TargetId];

        return value switch
        {
            Guid id => id,
            string text when Guid.TryParse(text, out Guid id) => id,
            _ => throw new SchedulerException(
                $"Job {context.JobDetail.Key} has no valid {TargetId}."),
        };
    }

    public static JobKind KindOf(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        object? value = context.MergedJobDataMap[Kind];

        if (value is JobKind kind)
        {
            return kind;
        }

        if (value is string text && Enum.TryParse(text, ignoreCase: true, out JobKind parsed))
        {
            return parsed;
        }

        throw new SchedulerException($"Job {context.JobDetail.Key} has no valid {Kind}.");
    }
}

/// <summary>
/// Детерминированные ключи задач: <c>stt:{turn_id}</c>, <c>nlu:{turn_id}</c>,
/// <c>tts:{answer_id}</c>. Повторная постановка находит ту же задачу.
/// </summary>
internal static class JobKeys
{
    public const string Group = "turns";
    public const string SystemGroup = "system";

    public static JobKey Job(JobKind kind, Guid targetId) =>
        new(KeyName(kind, targetId), Group);

    public static TriggerKey Trigger(JobKind kind, Guid targetId) =>
        new(KeyName(kind, targetId), Group);

    public static Type JobType(JobKind kind) =>
        kind switch
        {
            JobKind.Stt => typeof(SttJob),
            JobKind.Nlu => typeof(NluJob),
            JobKind.Tts => typeof(TtsJob),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    public static string KeyName(JobKind kind, Guid targetId) =>
        $"{Prefix(kind)}:{targetId}";

    public static JobKey ApplyJob => new("apply", SystemGroup);

    public static TriggerKey ApplyTrigger => new("apply-trigger", SystemGroup);

    public static JobKey ReconcilerJob => new("reconciler", SystemGroup);

    public static TriggerKey ReconcilerTrigger => new("reconciler-trigger", SystemGroup);

    private static string Prefix(JobKind kind) =>
        kind switch
        {
            JobKind.Stt => "stt",
            JobKind.Nlu => "nlu",
            JobKind.Tts => "tts",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
}
