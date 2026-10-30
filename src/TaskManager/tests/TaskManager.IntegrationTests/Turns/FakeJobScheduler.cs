using TaskManager.Application.Abstractions;

namespace TaskManager.IntegrationTests.Turns;

/// <summary>Планировщик-заглушка: запоминает постановки и не запускает задачи.</summary>
public sealed class FakeJobScheduler : IJobScheduler
{
    private readonly List<(JobKind Kind, Guid TargetId)> _scheduled = new();

    public IReadOnlyList<(JobKind Kind, Guid TargetId)> Scheduled => _scheduled;

    public void Reset() => _scheduled.Clear();

    public Task ScheduleAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        if (!_scheduled.Contains((kind, targetId)))
        {
            _scheduled.Add((kind, targetId));
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_scheduled.Contains((kind, targetId)));

    public Task RemoveAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        _scheduled.RemoveAll(item => item == (kind, targetId));
        return Task.CompletedTask;
    }
}
