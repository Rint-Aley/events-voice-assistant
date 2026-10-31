using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Отменяет черновик хода. Операция идемпотентна: отсутствие черновика не является
/// ошибкой, ход остаётся в <see cref="TurnState.NeedsClarification"/>, а неизвестный ход
/// даёт <c>TURN_NOT_FOUND</c>.
/// </summary>
public sealed class CancelDraftHandler
{
    private readonly ITurnRepository _turns;
    private readonly IDraftRepository _drafts;
    private readonly IUnitOfWork _unitOfWork;

    public CancelDraftHandler(
        ITurnRepository turns,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork)
    {
        _turns = turns;
        _drafts = drafts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CancelDraftResult>> HandleAsync(
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        if (turnId == Guid.Empty)
        {
            return Result<CancelDraftResult>.Failure(TurnErrors.IdRequired);
        }

        Turn? turn = await _turns.GetByIdAsync(turnId, cancellationToken);
        if (turn is null)
        {
            return Result<CancelDraftResult>.Failure(TurnErrors.TurnNotFound);
        }

        Draft? draft = turn.Draft;
        if (draft is null)
        {
            return Result<CancelDraftResult>.Success(
                new CancelDraftResult(TurnMessages.DraftCancelled));
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            turn.CancelDraft();
            await _drafts.RemoveAsync(draft, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Result<CancelDraftResult>.Success(
            new CancelDraftResult(TurnMessages.DraftCancelled));
    }
}
