using TaskManager.Application.Abstractions;
using TaskManager.Events.Application.Commands;
using TaskManager.Events.Application.Dtos;
using TaskManager.Events.Application.Scenarios;
using TaskManager.Events.Domain;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Plans;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Применяет pending-команды хода одной транзакцией: блокирует ход, читает команды по
/// <c>seq</c>, создаёт события через Events.Application, сохраняет ответ и переводит ход
/// в <see cref="TurnState.Completed"/>. Повторное применение завершённого хода не создаёт
/// дублей. После трёх неудачных попыток команды и ход помечаются <see cref="TurnState.Failed"/>.
/// </summary>
public sealed class ApplyTurnPlanHandler
{
    private readonly ITurnRepository _turns;
    private readonly IDomainCommandRepository _commands;
    private readonly IAnswerRepository _answers;
    private readonly CreateEventHandler _createEvent;
    private readonly IJobScheduler _scheduler;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ApplyTurnPlanHandler(
        ITurnRepository turns,
        IDomainCommandRepository commands,
        IAnswerRepository answers,
        CreateEventHandler createEvent,
        IJobScheduler scheduler,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _turns = turns;
        _commands = commands;
        _answers = answers;
        _createEvent = createEvent;
        _scheduler = scheduler;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<ApplyTurnPlanResult>> HandleAsync(
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        if (turnId == Guid.Empty)
        {
            return Result<ApplyTurnPlanResult>.Failure(TurnErrors.IdRequired);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            Turn? turn = await _turns.GetByIdForUpdateAsync(turnId, cancellationToken);
            if (turn is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<ApplyTurnPlanResult>.Failure(TurnErrors.TurnNotFound);
            }

            if (turn.State == TurnState.Completed)
            {
                // Повторный тик после успешного применения: отдаём уже готовый ответ,
                // ничего не создавая. Если постановка TTS сорвалась после коммита,
                // идемпотентная задача ставится повторно.
                Answer? existing = await _answers.GetByTurnIdAsync(turnId, cancellationToken);
                if (existing is null)
                {
                    // Ответ потерян: успех с null скрыл бы это от вызывающей стороны.
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    return Result<ApplyTurnPlanResult>.Failure(AnswerErrors.NotFound);
                }

                Result<ApplyTurnPlanResult> repeated = Result<ApplyTurnPlanResult>.Success(
                    new ApplyTurnPlanResult(turn.Id, turn.State, existing.Id, existing.Text));

                await _unitOfWork.RollbackAsync(cancellationToken);

                if (existing.AudioStatus == AnswerAudioStatus.Pending)
                {
                    await _scheduler.ScheduleAsync(JobKind.Tts, existing.Id, cancellationToken);
                }

                return repeated;
            }

            DateTimeOffset now = _clock.UtcNow;

            if (turn.State != TurnState.Applying)
            {
                // Ход уже терминальный (например, EXPIRED): pending-команды больше никогда
                // не применятся. Закрываем их, иначе тик будет выбирать их бесконечно.
                IReadOnlyList<DomainCommand> stranded = await _commands.ListPendingByTurnAsync(
                    turnId,
                    cancellationToken);

                foreach (DomainCommand command in stranded)
                {
                    Result closed = command.RegisterFailure(
                        $"Ход в состоянии {turn.State}: команда не будет применена.",
                        now,
                        now,
                        maxAttempts: 1);

                    if (closed.IsFailure)
                    {
                        await _unitOfWork.RollbackAsync(cancellationToken);
                        return Result<ApplyTurnPlanResult>.Failure(closed.Error!);
                    }
                }

                if (stranded.Count > 0)
                {
                    await _unitOfWork.CommitAsync(cancellationToken);
                }
                else
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                }

                return Result<ApplyTurnPlanResult>.Failure(TurnErrors.InvalidTransition);
            }

            IReadOnlyList<DomainCommand> pending = await _commands.ListPendingByTurnAsync(
                turnId,
                cancellationToken);

            if (pending.Count == 0)
            {
                // В APPLYING нечего применять: закрываем ход ошибкой, иначе он навсегда
                // занимает сессию через turns_one_active.
                Result emptyTransition = turn.Transition(
                    TurnTransition.ApplyFailed,
                    turn.StateVersion,
                    now);

                if (emptyTransition.IsFailure)
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    return Result<ApplyTurnPlanResult>.Failure(emptyTransition.Error!);
                }

                await _unitOfWork.CommitAsync(cancellationToken);

                return Result<ApplyTurnPlanResult>.Failure(TurnErrors.DomainApplyFailed);
            }

            List<PreparedCommand> prepared;

            try
            {
                // Валидация до первой записи: ожидаемая ошибка команды не должна оставлять
                // в change tracker частично созданные события и ответы.
                prepared = Prepare(pending);
            }
            catch (ApplyFailureException failure)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return await RegisterFailureAsync(turnId, failure.Message, cancellationToken);
            }

            List<EventDto> created = new();
            List<string> warnings = new();

            foreach (PreparedCommand item in prepared)
            {
                Result<CreateEventResult> applied = await _createEvent.HandleAsync(
                    item.Command,
                    cancellationToken);

                if (applied.IsFailure)
                {
                    // Ожидаемые ошибки отсеяны Prepare. Транзакция откатывается целиком:
                    // частично созданные события не должны пережить неудачную попытку.
                    // Попытка регистрируется всегда, чтобы работал backoff и лимит 3 попыток.
                    await _unitOfWork.RollbackAsync(cancellationToken);

                    return await RegisterFailureAsync(turnId, applied.Error!.Message, cancellationToken);
                }

                created.Add(applied.Value.Event);
                warnings.AddRange(applied.Value.Conflicts
                    .Select(conflict => AnswerText.MeetingConflict(conflict.Description))
                    .Where(warning => !warnings.Contains(warning)));

                item.DomainCommand.MarkApplied(now);
            }

            DateOnly today = DateOnly.FromDateTime(_clock.LocalNow.DateTime);
            string text = AnswerText.Created(created, today);
            if (warnings.Count > 0)
            {
                // В тексте — первая встреча, в warnings — все пересечения.
                text = $"{text} {warnings[0]}";
            }

            Result<Answer> answer = Answer.Create(
                turn.Id,
                text,
                now,
                warnings: warnings);

            if (answer.IsFailure)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<ApplyTurnPlanResult>.Failure(answer.Error!);
            }

            Result transition = turn.Transition(
                TurnTransition.CommandsApplied,
                turn.StateVersion,
                now);

            if (transition.IsFailure)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<ApplyTurnPlanResult>.Failure(transition.Error!);
            }

            await _answers.AddAsync(answer.Value, cancellationToken);

            short position = 0;
            foreach (EventDto @event in created)
            {
                Result<AnswerEvent> link = AnswerEvent.Create(
                    answer.Value.Id,
                    @event.Id,
                    position);

                if (link.IsFailure)
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    return Result<ApplyTurnPlanResult>.Failure(link.Error!);
                }

                await _answers.AddEventAsync(link.Value, cancellationToken);
                position++;
            }

            await _unitOfWork.CommitAsync(cancellationToken);

            // Постановка после коммита: сбой постановки не откатывает ответ,
            // reconciler добьёт задачу.
            await _scheduler.ScheduleAsync(JobKind.Tts, answer.Value.Id, cancellationToken);

            return Result<ApplyTurnPlanResult>.Success(new ApplyTurnPlanResult(
                turn.Id,
                turn.State,
                answer.Value.Id,
                answer.Value.Text));
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static List<PreparedCommand> Prepare(IReadOnlyList<DomainCommand> commands)
    {
        var prepared = new List<PreparedCommand>(commands.Count);

        foreach (DomainCommand command in commands)
        {
            if (command.Operation != DomainCommandOperation.EventCreate)
            {
                throw new ApplyFailureException(
                    $"Операция {command.Operation} в MVP не поддерживается.");
            }

            if (!EventCreatePayloadJson.TryDeserialize(command.Payload, out EventCreatePayload? payload)
                || payload is null)
            {
                throw new ApplyFailureException("Полезная нагрузка команды не разобрана.");
            }

            if (!TryMapKind(payload.Kind, out EventKind kind))
            {
                throw new ApplyFailureException("Неизвестный тип создаваемой записи.");
            }

            var create = new CreateEventCommand(
                kind,
                payload.Description,
                payload.SourceText,
                payload.StartsOn,
                payload.StartsAt,
                payload.Duration,
                payload.IsDeadline);

            // Вектор из payload здесь не используется: Event.Create принимает только слоты,
            // запись embedding в events — отдельная задача.

            Result<Event> validated = Event.Create(
                create.Kind,
                create.Description,
                create.SourceText,
                create.StartsOn,
                create.StartsAt,
                create.Duration,
                create.IsDeadline);

            if (validated.IsFailure)
            {
                throw new ApplyFailureException(validated.Error!.Message);
            }

            prepared.Add(new PreparedCommand(command, create));
        }

        return prepared;
    }

    private async Task<Result<ApplyTurnPlanResult>> RegisterFailureAsync(
        Guid turnId,
        string error,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            Turn? turn = await _turns.GetByIdForUpdateAsync(turnId, cancellationToken);
            if (turn is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<ApplyTurnPlanResult>.Failure(TurnErrors.TurnNotFound);
            }

            IReadOnlyList<DomainCommand> pending = await _commands.ListPendingByTurnAsync(
                turnId,
                cancellationToken);

            if (turn.State != TurnState.Applying)
            {
                // Пока сценарий откатывался, ход мог стать терминальным. Тогда команды
                // больше не применятся и должны быть закрыты, иначе тик зациклится.
                foreach (DomainCommand command in pending)
                {
                    Result closed = command.RegisterFailure(
                        error,
                        now,
                        now,
                        maxAttempts: 1);

                    if (closed.IsFailure)
                    {
                        await _unitOfWork.RollbackAsync(cancellationToken);
                        return Result<ApplyTurnPlanResult>.Failure(closed.Error!);
                    }
                }

                if (pending.Count > 0)
                {
                    await _unitOfWork.CommitAsync(cancellationToken);
                }
                else
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                }

                return Result<ApplyTurnPlanResult>.Failure(TurnErrors.DomainApplyFailed);
            }

            if (pending.Count == 0)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<ApplyTurnPlanResult>.Failure(TurnErrors.DomainApplyFailed);
            }

            bool exhausted = false;

            foreach (DomainCommand command in pending)
            {
                int attempt = command.Attempts + 1;
                Result registered = command.RegisterFailure(
                    error,
                    now + Backoff(attempt),
                    now);

                if (registered.IsFailure)
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    return Result<ApplyTurnPlanResult>.Failure(registered.Error!);
                }

                exhausted |= command.Status == DomainCommandStatus.Failed;
            }

            if (exhausted)
            {
                Result transition = turn.Transition(
                    TurnTransition.ApplyFailed,
                    turn.StateVersion,
                    now);

                if (transition.IsFailure)
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    return Result<ApplyTurnPlanResult>.Failure(transition.Error!);
                }
            }

            await _unitOfWork.CommitAsync(cancellationToken);

            return Result<ApplyTurnPlanResult>.Failure(TurnErrors.DomainApplyFailed);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(attempt switch
        {
            <= 1 => 1,
            2 => 2,
            _ => 4,
        });

    private static bool TryMapKind(string? kind, out EventKind eventKind)
    {
        switch (kind)
        {
            case "task":
                eventKind = EventKind.Task;
                return true;
            case "reminder":
                eventKind = EventKind.Reminder;
                return true;
            case "meeting":
                eventKind = EventKind.Meeting;
                return true;
            default:
                eventKind = default;
                return false;
        }
    }

    private sealed record PreparedCommand(
        DomainCommand DomainCommand,
        CreateEventCommand Command);

    private sealed class ApplyFailureException(string message) : Exception(message);
}
