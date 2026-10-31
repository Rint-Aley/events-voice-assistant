using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Применяет ответ <c>stt</c>: пустой текст закрывает ход кодом <c>EMPTY_TRANSCRIPT</c>,
/// иначе текст сохраняется, ход переходит в <see cref="TurnState.Interpreting"/>, а после
/// коммита ставится <c>nlu</c>. Поздний результат отбрасывается проверкой CAS.
/// </summary>
public sealed class HandleSttResultHandler
{
    private readonly ITurnRepository _turns;
    private readonly IJobScheduler _scheduler;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public HandleSttResultHandler(
        ITurnRepository turns,
        IJobScheduler scheduler,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _turns = turns;
        _scheduler = scheduler;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SubmitTurnResult>> HandleAsync(
        SttResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        Turn? turn = await _turns.GetByIdAsync(result.TurnId, cancellationToken);
        if (turn is null)
        {
            return Result<SubmitTurnResult>.Failure(TurnErrors.TurnNotFound);
        }

        DateTimeOffset now = _clock.UtcNow;
        bool empty = result.Empty;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            Result transition = empty
                ? turn.Transition(TurnTransition.EmptyTranscript, turn.StateVersion, now)
                : turn.ApplySttResult(result.Text, result.Confidence, turn.StateVersion, now);

            if (transition.IsFailure)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<SubmitTurnResult>.Failure(transition.Error!);
            }

            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (!empty)
        {
            await _scheduler.ScheduleAsync(JobKind.Nlu, turn.Id, cancellationToken);
        }

        return Result<SubmitTurnResult>.Success(new SubmitTurnResult(turn.Id, turn.State));
    }
}
