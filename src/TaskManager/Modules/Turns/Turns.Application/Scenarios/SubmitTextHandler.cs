using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Configuration;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Принимает текстовый запрос: проверяет ключ идемпотентности, отсутствие активного хода,
/// создаёт ход в <see cref="TurnState.Interpreting"/> и после коммита ставит <c>nlu</c>.
/// </summary>
public sealed class SubmitTextHandler
{
    private readonly ITurnRepository _turns;
    private readonly IJobScheduler _scheduler;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly TurnIntakeOptions _options;

    public SubmitTextHandler(
        ITurnRepository turns,
        IJobScheduler scheduler,
        IUnitOfWork unitOfWork,
        IClock clock,
        TurnIntakeOptions? options = null)
    {
        _turns = turns;
        _scheduler = scheduler;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options ?? TurnIntakeOptions.Default;
    }

    public async Task<Result<SubmitTurnResult>> HandleAsync(
        SubmitTextCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SessionId == Guid.Empty)
        {
            return Result<SubmitTurnResult>.Failure(TurnErrors.SessionIdRequired);
        }

        if (command.IdempotencyKey == Guid.Empty)
        {
            return Result<SubmitTurnResult>.Failure(TurnErrors.IdempotencyKeyRequired);
        }

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return Result<SubmitTurnResult>.Failure(TurnErrors.TranscriptRequired);
        }

        Turn? existing = await _turns.GetByIdempotencyKeyAsync(
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return Result<SubmitTurnResult>.Success(
                new SubmitTurnResult(existing.Id, existing.State));
        }

        Turn? active = await _turns.GetActiveBySessionAsync(command.SessionId, cancellationToken);
        if (active is not null)
        {
            return Result<SubmitTurnResult>.Failure(TurnErrors.SessionBusy);
        }

        DateTimeOffset now = _clock.UtcNow;
        Result<Turn> created = Turn.Create(
            command.SessionId,
            TurnState.Interpreting,
            command.IdempotencyKey,
            now + _options.Deadline,
            now,
            text: command.Text);

        if (created.IsFailure)
        {
            return Result<SubmitTurnResult>.Failure(created.Error!);
        }

        Turn turn = created.Value;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await _turns.AddAsync(turn, cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        // Постановка — после коммита. Постановка идемпотентна, reconciler добьёт пропуск.
        await _scheduler.ScheduleAsync(JobKind.Nlu, turn.Id, cancellationToken);

        return Result<SubmitTurnResult>.Success(new SubmitTurnResult(turn.Id, turn.State));
    }
}
