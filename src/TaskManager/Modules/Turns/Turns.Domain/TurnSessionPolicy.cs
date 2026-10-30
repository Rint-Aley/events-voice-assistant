using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>
/// Инвариант сессии: одновременно активным может быть только один ход. Повторная попытка
/// получить второй активный ход возвращает <see cref="TurnErrors.SessionBusy"/>.
/// </summary>
public static class TurnSessionPolicy
{
    /// <summary>
    /// Проверяет, что в сессии нет активного хода. Завершённые ходы и ходы других сессий
    /// не мешают создать новый.
    /// </summary>
    public static Result EnsureCanStart(Guid sessionId, IEnumerable<Turn> sessionTurns)
    {
        ArgumentNullException.ThrowIfNull(sessionTurns);

        if (sessionId == Guid.Empty)
        {
            return Result.Failure(TurnErrors.SessionIdRequired);
        }

        foreach (var turn in sessionTurns)
        {
            if (turn.SessionId == sessionId && turn.IsActive)
            {
                return Result.Failure(TurnErrors.SessionBusy);
            }
        }

        return Result.Success();
    }
}
