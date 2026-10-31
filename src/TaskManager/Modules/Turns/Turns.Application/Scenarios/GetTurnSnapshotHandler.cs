using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Application.Queries;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Отдаёт снапшот хода: состояние, текст, ответ, события, <c>total_count</c>,
/// <c>warnings</c> и аудио. Чтение идёт через query-порт без записи.
/// </summary>
public sealed class GetTurnSnapshotHandler
{
    private readonly ITurnQuery _query;

    public GetTurnSnapshotHandler(ITurnQuery query)
    {
        _query = query;
    }

    public async Task<Result<TurnSnapshot>> HandleAsync(
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        if (turnId == Guid.Empty)
        {
            return Result<TurnSnapshot>.Failure(TurnErrors.IdRequired);
        }

        TurnSnapshot? snapshot = await _query.GetSnapshotAsync(turnId, cancellationToken);

        return snapshot is null
            ? Result<TurnSnapshot>.Failure(TurnErrors.TurnNotFound)
            : Result<TurnSnapshot>.Success(snapshot);
    }
}
