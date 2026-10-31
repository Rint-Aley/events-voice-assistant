using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Закрывает ход ожидаемой ошибкой: <c>STT_FAILED</c>, <c>NLU_FAILED</c>,
/// <c>DOMAIN_APPLY_FAILED</c> или <c>DEADLINE_EXCEEDED</c>. Финальный обработчик
/// планировщика вызывает сценарий после исчерпания попыток. Повтор безопасен: терминальный
/// ход не меняется.
/// </summary>
public sealed class FailTurnHandler
{
    private static readonly TurnTransition[] AllowedTransitions =
    [
        TurnTransition.SttFailed,
        TurnTransition.NluFailed,
        TurnTransition.ApplyFailed,
        TurnTransition.DeadlineExceeded,
    ];

    private readonly ITurnRepository _turns;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public FailTurnHandler(
        ITurnRepository turns,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _turns = turns;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TurnCloseResult>> HandleAsync(
        Guid turnId,
        TurnTransition transition,
        CancellationToken cancellationToken = default)
    {
        if (turnId == Guid.Empty)
        {
            return Result<TurnCloseResult>.Failure(TurnErrors.IdRequired);
        }

        if (!AllowedTransitions.Contains(transition))
        {
            return Result<TurnCloseResult>.Failure(TurnErrors.InvalidTransition);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            Turn? turn = await _turns.GetByIdForUpdateAsync(turnId, cancellationToken);
            if (turn is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<TurnCloseResult>.Failure(TurnErrors.TurnNotFound);
            }

            // Поздний результат после EXPIRED/FAILED/COMPLETED отбрасывается без ошибки:
            // финальный обработчик может прийти уже после того, как ход закрыл другой сценарий.
            if (turn.IsTerminal)
            {
                TurnState state = turn.State;
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<TurnCloseResult>.Success(
                    new TurnCloseResult(state, Changed: false));
            }

            Result<TurnState> transitioned = turn.Transition(
                transition,
                turn.StateVersion,
                _clock.UtcNow);

            if (transitioned.IsFailure)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<TurnCloseResult>.Failure(transitioned.Error!);
            }

            await _unitOfWork.CommitAsync(cancellationToken);

            return Result<TurnCloseResult>.Success(
                new TurnCloseResult(transitioned.Value, Changed: true));
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
