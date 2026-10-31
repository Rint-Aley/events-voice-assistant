namespace TaskManager.Application.Abstractions;

/// <summary>
/// Порт планировщика. Application не знает типов Quartz; ключ задачи детерминирован
/// по виду задачи и цели, поэтому повторная постановка не создаёт вторую задачу.
/// </summary>
public interface IJobScheduler
{
    /// <summary>
    /// Ставит задачу, если задачи с таким ключом ещё нет. Постановка безопасна для повтора
    /// и выполняется после коммита транзакции.
    /// </summary>
    Task ScheduleAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>Проверяет, есть ли задача с ключом вида <c>{kind}:{targetId}</c>.</summary>
    Task<bool> ExistsAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default);
}
