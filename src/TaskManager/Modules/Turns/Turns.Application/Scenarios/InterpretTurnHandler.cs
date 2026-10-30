using System.Globalization;
using System.Text.Json;
using TaskManager.Application.Abstractions;
using TaskManager.Events.Application.Queries;
using TaskManager.Events.Application.Scenarios;
using TaskManager.Events.Domain;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Plans;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Вызывает <c>nlu</c>, нормализует сущности и переводит ход в чтение, запись или
/// уточнение. Даты и время приходят строками: невалидные значения удаляются из слотов и
/// превращаются в общий вопрос. Прошлое время сохраняет черновик. Описание без валидного
/// эмбеддинга закрывает ход кодом <c>NLU_FAILED</c>.
/// </summary>
public sealed class InterpretTurnHandler
{
    private static readonly JsonSerializerOptions PlanJson = new(JsonSerializerDefaults.Web);

    private readonly ITurnRepository _turns;
    private readonly IDraftRepository _drafts;
    private readonly IAnswerRepository _answers;
    private readonly IDomainCommandRepository _commands;
    private readonly INluClient _nlu;
    private readonly ListPeriodHandler _listPeriod;
    private readonly IJobScheduler _scheduler;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public InterpretTurnHandler(
        ITurnRepository turns,
        IDraftRepository drafts,
        IAnswerRepository answers,
        IDomainCommandRepository commands,
        INluClient nlu,
        ListPeriodHandler listPeriod,
        IJobScheduler scheduler,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _turns = turns;
        _drafts = drafts;
        _answers = answers;
        _commands = commands;
        _nlu = nlu;
        _listPeriod = listPeriod;
        _scheduler = scheduler;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<InterpretTurnResult>> HandleAsync(
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        if (turnId == Guid.Empty)
        {
            return Result<InterpretTurnResult>.Failure(TurnErrors.IdRequired);
        }

        Turn? turn = await _turns.GetByIdAsync(turnId, cancellationToken);
        if (turn is null)
        {
            return Result<InterpretTurnResult>.Failure(TurnErrors.TurnNotFound);
        }

        if (turn.State != TurnState.Interpreting)
        {
            return Result<InterpretTurnResult>.Failure(TurnErrors.InvalidTransition);
        }

        Turn? previous = await _turns.GetLatestClarificationAsync(
            turn.SessionId,
            cancellationToken);
        Draft? previousDraft = previous?.Draft;

        DateTimeOffset now = _clock.UtcNow;
        NluResponse response = await _nlu.InterpretAsync(
            BuildRequest(turn, previousDraft, _clock.LocalNow),
            cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            Result<InterpretTurnResult> applied = await ApplyAsync(
                turn,
                previous,
                previousDraft,
                response,
                now,
                cancellationToken);

            if (applied.IsFailure)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return applied;
            }

            await _unitOfWork.CommitAsync(cancellationToken);

            // Применение команд плана записи — отдельный сценарий. Немедленную попытку
            // после коммита и страховочный тик запускает планировщик (задача Quartz),
            // поэтому здесь фиксируются только plan и domain_commands в APPLYING.
            if (applied.Value.AnswerId is { } answerId)
            {
                await _scheduler.ScheduleAsync(JobKind.Tts, answerId, cancellationToken);
            }

            return applied;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<Result<InterpretTurnResult>> ApplyAsync(
        Turn turn,
        Turn? previous,
        Draft? previousDraft,
        NluResponse response,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string plan = JsonSerializer.Serialize(response, PlanJson);
        TurnIntent? intent = ParseAction(response.Action);

        if (intent is null)
        {
            return await FailNluAsync(turn, now);
        }

        Interpretation interpretation = Interpret(response, intent.Value, previousDraft);

        bool descriptionSupplied = !string.IsNullOrWhiteSpace(
            Normalize(response.Entities?.Description));
        bool requiresEmbedding = intent.Value is
            TurnIntent.CreateTask or TurnIntent.CreateReminder or TurnIntent.CreateMeeting
                ? !string.IsNullOrWhiteSpace(interpretation.Description)
                : descriptionSupplied;

        if (requiresEmbedding && !IsEmbeddingValid(response.Embedding))
        {
            return await FailNluAsync(turn, now);
        }

        if (previousDraft is not null && DraftPolicy.ShouldReset(intent.Value))
        {
            await RemoveDraftAsync(previous!, previousDraft, cancellationToken);
        }

        if (intent.Value is TurnIntent.ListPeriod)
        {
            return await ApplyReadPlanAsync(
                turn,
                plan,
                intent.Value,
                response.Entities,
                now,
                cancellationToken);
        }

        if (intent.Value is TurnIntent.Search or TurnIntent.Unknown)
        {
            TurnTransition transition = intent.Value == TurnIntent.Unknown
                ? TurnTransition.NotUnderstood
                : TurnTransition.ReadPlan;

            return ApplyPlan(turn, transition, plan, intent.Value, now);
        }

        if (!interpretation.NeedsClarification)
        {
            // Слоты предыдущего уточнения перенесены в итоговый запрос: активным остаётся
            // один черновик, поэтому старый удаляется вместе с переходом в запись.
            if (previousDraft is not null)
            {
                await RemoveDraftAsync(previous!, previousDraft, cancellationToken);
            }

            Result<InterpretTurnResult> written = ApplyPlan(
                turn,
                TurnTransition.WritePlan,
                plan,
                intent.Value,
                now);

            if (written.IsFailure)
            {
                return written;
            }

            Result<DomainCommand> command = BuildEventCreateCommand(
                turn,
                interpretation,
                response.Embedding,
                previousDraft,
                now);

            if (command.IsFailure)
            {
                return Result<InterpretTurnResult>.Failure(command.Error!);
            }

            await _commands.AddAsync(command.Value, cancellationToken);

            return written;
        }

        // Два раунда вопросов уже заданы: третий ответ завершает ход, черновик удаляется.
        if (previousDraft is not null
            && previousDraft.ClarificationRound >= Draft.MaxClarificationRounds)
        {
            Result<InterpretTurnResult> completed = ApplyPlan(
                turn,
                TurnTransition.NotUnderstood,
                plan,
                intent.Value,
                now);

            if (completed.IsFailure)
            {
                return completed;
            }

            await RemoveDraftAsync(previous!, previousDraft, cancellationToken);

            Result<Answer> answered = await AddAnswerAsync(
                turn,
                TurnMessages.ClarificationExhausted,
                now,
                cancellationToken);

            return answered.IsFailure
                ? Result<InterpretTurnResult>.Failure(answered.Error!)
                : Result<InterpretTurnResult>.Success(
                    completed.Value with { AnswerId = answered.Value.Id });
        }

        Result<InterpretTurnResult> clarified = ApplyPlan(
            turn,
            TurnTransition.ClarifyPlan,
            plan,
            intent.Value,
            now);

        if (clarified.IsFailure)
        {
            return clarified;
        }

        int round = (previousDraft?.ClarificationRound ?? 0) + 1;
        Result<Draft> draft = BuildDraft(turn, previousDraft, interpretation, round, now);
        if (draft.IsFailure)
        {
            return Result<InterpretTurnResult>.Failure(draft.Error!);
        }

        Result attached = turn.AttachDraft(draft.Value);
        if (attached.IsFailure)
        {
            return Result<InterpretTurnResult>.Failure(attached.Error!);
        }

        // Новый раунд заменяет черновик предыдущего хода: активным остаётся один черновик.
        if (previousDraft is not null)
        {
            await RemoveDraftAsync(previous!, previousDraft, cancellationToken);
        }

        await _drafts.AddAsync(draft.Value, cancellationToken);

        Result<Answer> answer = await AddAnswerAsync(
            turn,
            interpretation.Question ?? TurnMessages.DescriptionQuestion,
            now,
            cancellationToken);

        return answer.IsFailure
            ? Result<InterpretTurnResult>.Failure(answer.Error!)
            : Result<InterpretTurnResult>.Success(
                clarified.Value with { AnswerId = answer.Value.Id });
    }

    private static Result<InterpretTurnResult> ApplyPlan(
        Turn turn,
        TurnTransition transition,
        string plan,
        TurnIntent intent,
        DateTimeOffset now)
    {
        Result applied = turn.ApplyPlan(transition, plan, turn.StateVersion, now);

        return applied.IsFailure
            ? Result<InterpretTurnResult>.Failure(applied.Error!)
            : Result<InterpretTurnResult>.Success(
                new InterpretTurnResult(turn.Id, turn.State, intent));
    }

    private async Task<Result<InterpretTurnResult>> ApplyReadPlanAsync(
        Turn turn,
        string plan,
        TurnIntent intent,
        NluEntities? entities,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Без валидного периода план list_period неполон: это невалидный контракт NLU,
        // а не пустой успешный список. Ход закрывается NLU_FAILED, ответа и TTS нет.
        if (!TryParsePeriod(entities, out DateOnly from, out DateOnly to))
        {
            return await FailNluAsync(turn, now);
        }

        // Неизвестный type_filter — тоже нарушение контракта, а не «все типы».
        if (!TryParseTypeFilter(entities?.TypeFilter, out EventTypeFilter? typeFilter))
        {
            return await FailNluAsync(turn, now);
        }

        // Перевёрнутый период — ошибка данных, а не ошибка сценария: завершаем ход
        // просьбой переформулировать, чтобы он не завис в INTERPRETING.
        if (to < from)
        {
            Result<InterpretTurnResult> notUnderstood = ApplyPlan(
                turn,
                TurnTransition.NotUnderstood,
                plan,
                intent,
                now);

            if (notUnderstood.IsFailure)
            {
                return notUnderstood;
            }

            Result<Answer> refusal = await AddAnswerAsync(
                turn,
                TurnMessages.ClarificationExhausted,
                now,
                cancellationToken);

            return refusal.IsFailure
                ? Result<InterpretTurnResult>.Failure(refusal.Error!)
                : Result<InterpretTurnResult>.Success(
                    notUnderstood.Value with { AnswerId = refusal.Value.Id });
        }

        Result<ListPeriodResult> list = await _listPeriod.HandleAsync(
            new ListPeriodQuery(from, to, typeFilter),
            cancellationToken);

        if (list.IsFailure)
        {
            return Result<InterpretTurnResult>.Failure(list.Error!);
        }

        DateOnly today = DateOnly.FromDateTime(_clock.LocalNow.DateTime);
        string text = AnswerText.List(
            typeFilter,
            from,
            to,
            today,
            list.Value.Events,
            list.Value.TotalCount);

        Result<Answer> answer = Answer.Create(
            turn.Id,
            text,
            now,
            totalCount: list.Value.TotalCount);

        if (answer.IsFailure)
        {
            return Result<InterpretTurnResult>.Failure(answer.Error!);
        }

        Result<InterpretTurnResult> completed = ApplyPlan(
            turn,
            TurnTransition.ReadPlan,
            plan,
            intent,
            now);

        if (completed.IsFailure)
        {
            return completed;
        }

        await _answers.AddAsync(answer.Value, cancellationToken);

        short position = 0;
        foreach (var @event in list.Value.Events)
        {
            Result<AnswerEvent> link = AnswerEvent.Create(
                answer.Value.Id,
                @event.Id,
                position);

            if (link.IsFailure)
            {
                return Result<InterpretTurnResult>.Failure(link.Error!);
            }

            await _answers.AddEventAsync(link.Value, cancellationToken);
            position++;
        }

        return Result<InterpretTurnResult>.Success(
            completed.Value with { AnswerId = answer.Value.Id });
    }

    private static Task<Result<InterpretTurnResult>> FailNluAsync(
        Turn turn,
        DateTimeOffset now)
    {
        Result transition = turn.Transition(TurnTransition.NluFailed, turn.StateVersion, now);

        Result<InterpretTurnResult> result = transition.IsFailure
            ? Result<InterpretTurnResult>.Failure(transition.Error!)
            : Result<InterpretTurnResult>.Success(
                new InterpretTurnResult(turn.Id, turn.State, Intent: null));

        return Task.FromResult(result);
    }

    private async Task<Result<Answer>> AddAnswerAsync(
        Turn turn,
        string text,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Result<Answer> answer = Answer.Create(turn.Id, text, now);
        if (answer.IsFailure)
        {
            return answer;
        }

        await _answers.AddAsync(answer.Value, cancellationToken);

        return answer;
    }

    private async Task RemoveDraftAsync(
        Turn owner,
        Draft draft,
        CancellationToken cancellationToken)
    {
        owner.CancelDraft();
        await _drafts.RemoveAsync(draft, cancellationToken);
    }

    private static Result<Draft> BuildDraft(
        Turn turn,
        Draft? previous,
        Interpretation interpretation,
        int round,
        DateTimeOffset now)
    {
        Result<Draft> created = Draft.Create(
            turn.Id,
            now,
            kind: interpretation.Kind,
            description: interpretation.Description,
            sourceText: previous?.SourceText,
            startsOn: interpretation.StartsOn,
            startsAt: interpretation.StartsAt,
            duration: interpretation.Duration,
            isDeadline: interpretation.IsDeadline);

        if (created.IsFailure)
        {
            return created;
        }

        Draft draft = created.Value;
        Result source = draft.AddSourceText(turn.Text, now);
        if (source.IsFailure)
        {
            return Result<Draft>.Failure(source.Error!);
        }

        for (int step = 0; step < round; step++)
        {
            Result began = draft.BeginClarificationRound(now);
            if (began.IsFailure)
            {
                return Result<Draft>.Failure(began.Error!);
            }
        }

        return Result<Draft>.Success(draft);
    }

    private static Result<DomainCommand> BuildEventCreateCommand(
        Turn turn,
        Interpretation interpretation,
        NluEmbedding? embedding,
        Draft? previousDraft,
        DateTimeOffset now)
    {
        if (interpretation.Kind is not { } kind)
        {
            return Result<DomainCommand>.Failure(DraftErrors.KindInvalid);
        }

        var payload = new EventCreatePayload(
            ToPayloadKind(kind),
            interpretation.Description,
            CombineSourceText(previousDraft?.SourceText, turn.Text),
            interpretation.StartsOn,
            interpretation.StartsAt,
            interpretation.Duration,
            interpretation.IsDeadline,
            embedding);

        return DomainCommand.Create(
            turn.Id,
            seq: 0,
            DomainCommandOperation.EventCreate,
            EventCreatePayloadJson.Serialize(payload),
            now);
    }

    private static bool TryParsePeriod(
        NluEntities? entities,
        out DateOnly from,
        out DateOnly to)
    {
        from = default;
        to = default;

        return !string.IsNullOrWhiteSpace(entities?.PeriodFrom)
            && !string.IsNullOrWhiteSpace(entities.PeriodTo)
            && TryParseDate(entities.PeriodFrom, out from)
            && TryParseDate(entities.PeriodTo, out to);
    }

    private static bool TryParseTypeFilter(string? value, out EventTypeFilter? filter)
    {
        switch (Normalize(value))
        {
            case null:
                filter = null;
                return true;
            case "meeting":
                filter = EventTypeFilter.Meeting;
                return true;
            case "task":
                filter = EventTypeFilter.Task;
                return true;
            case "reminder":
                filter = EventTypeFilter.Reminder;
                return true;
            default:
                filter = null;
                return false;
        }
    }

    private static string ToPayloadKind(DraftKind kind) =>
        kind switch
        {
            DraftKind.Task => "task",
            DraftKind.Reminder => "reminder",
            DraftKind.Meeting => "meeting",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static string? CombineSourceText(string? previous, string? current)
    {
        string? left = Normalize(previous);
        string? right = Normalize(current);

        if (left is null)
        {
            return right;
        }

        return right is null ? left : $"{left}; {right}";
    }

    private static NluDraft? BuildDraftDto(Draft? draft)
    {
        if (draft is null)
        {
            return null;
        }

        string? startsOn = draft.StartsOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string? startsAt = draft.StartsAt?.ToString("HH:mm", CultureInfo.InvariantCulture);
        int? durationMinutes = draft.Duration is { } duration
            ? (int)duration.TotalMinutes
            : null;

        // Контракт не допускает пустой объект и null-поля: незаполненный черновик
        // передаётся как отсутствие черновика.
        bool hasSlots = draft.Kind.HasValue
            || !string.IsNullOrWhiteSpace(draft.Description)
            || startsOn is not null
            || startsAt is not null
            || durationMinutes is not null;

        return hasSlots
            ? new NluDraft(
                draft.Kind,
                draft.Description,
                startsOn,
                startsAt,
                durationMinutes)
            : null;
    }

    private NluRequest BuildRequest(Turn turn, Draft? draft, DateTimeOffset now)
    {
        return new NluRequest(
            turn.Id,
            turn.Text ?? string.Empty,
            now,
            _clock.TimeZone.Id,
            BuildDraftDto(draft));
    }

    private Interpretation Interpret(
        NluResponse response,
        TurnIntent intent,
        Draft? previousDraft)
    {
        NluEntities? entities = response.Entities;

        DraftKind? kind = intent switch
        {
            TurnIntent.CreateTask => DraftKind.Task,
            TurnIntent.CreateReminder => DraftKind.Reminder,
            TurnIntent.CreateMeeting => DraftKind.Meeting,
            _ => previousDraft?.Kind,
        };

        string? description = Normalize(entities?.Description) ?? previousDraft?.Description;

        DateOnly? startsOn = previousDraft?.StartsOn;
        TimeOnly? startsAt = previousDraft?.StartsAt;
        bool invalidDate = false;
        bool invalidTime = false;

        if (!string.IsNullOrWhiteSpace(entities?.StartsOn))
        {
            if (TryParseDate(entities.StartsOn, out DateOnly date))
            {
                startsOn = date;
            }
            else
            {
                invalidDate = true;
                startsOn = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(entities?.StartsAt))
        {
            if (TryParseTime(entities.StartsAt, out TimeOnly time))
            {
                startsAt = time;
            }
            else
            {
                invalidTime = true;
                startsAt = null;
            }
        }

        // Время без даты не нормализуется; невалидная дата сбрасывает и время.
        if (invalidDate)
        {
            startsOn = null;
            startsAt = null;
        }
        else if (!startsOn.HasValue)
        {
            startsAt = null;
        }

        bool timeWasGiven = !string.IsNullOrWhiteSpace(entities?.StartsAt);

        int? durationMinutes = entities?.DurationMinutes;
        if (durationMinutes is null && previousDraft?.Duration is { } previousDuration)
        {
            durationMinutes = (int)previousDuration.TotalMinutes;
        }

        TimeSpan? duration = kind == DraftKind.Meeting
            && startsAt.HasValue
            && durationMinutes is > 0
                ? TimeSpan.FromMinutes(durationMinutes.Value)
                : null;

        bool isDeadline = kind == DraftKind.Task
            && (entities?.IsDeadline ?? previousDraft?.IsDeadline ?? false);

        bool requiresDateAndTime = kind is DraftKind.Reminder or DraftKind.Meeting;
        bool hasDate = startsOn.HasValue;
        bool hasTime = startsAt.HasValue;
        bool isPast = hasDate && hasTime && IsPast(startsOn!.Value, startsAt!.Value);

        List<string> missing = new();
        if (string.IsNullOrWhiteSpace(description))
        {
            missing.Add("description");
        }

        if (invalidDate || (requiresDateAndTime && !hasDate) || (timeWasGiven && !hasDate))
        {
            missing.Add("date");
        }

        if (invalidTime || (requiresDateAndTime && !hasTime))
        {
            missing.Add("time");
        }

        if (isPast && !missing.Contains("time"))
        {
            missing.Add("time");
        }

        bool needsClarification = intent == TurnIntent.Clarify || missing.Count > 0;

        string? question = BuildQuestion(
            response,
            intent,
            description,
            requiresDateAndTime,
            hasDate,
            hasTime,
            invalidDate,
            invalidTime,
            isPast,
            timeWasGiven);

        return new Interpretation(
            kind,
            description,
            startsOn,
            startsAt,
            duration,
            isDeadline,
            question,
            needsClarification);
    }

    private static string? BuildQuestion(
        NluResponse response,
        TurnIntent intent,
        string? description,
        bool requiresDateAndTime,
        bool hasDate,
        bool hasTime,
        bool invalidDate,
        bool invalidTime,
        bool isPast,
        bool timeWasGiven)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return TurnMessages.DescriptionQuestion;
        }

        if (invalidDate)
        {
            return TurnMessages.InvalidDateQuestion;
        }

        if (invalidTime)
        {
            return TurnMessages.InvalidTimeQuestion;
        }

        if (isPast)
        {
            return TurnMessages.PastTimeQuestion;
        }

        if (requiresDateAndTime && !hasDate && !hasTime)
        {
            return TurnMessages.DateTimeQuestion(description);
        }

        if (requiresDateAndTime && !hasDate)
        {
            return TurnMessages.DateQuestion(description);
        }

        if (requiresDateAndTime && !hasTime)
        {
            return TurnMessages.TimeQuestion(description);
        }

        if (timeWasGiven && !hasDate)
        {
            return TurnMessages.DateQuestion(description);
        }

        if (!string.IsNullOrWhiteSpace(response.Question))
        {
            return response.Question.Trim();
        }

        return intent == TurnIntent.Clarify ? TurnMessages.DescriptionQuestion : null;
    }

    private bool IsPast(DateOnly startsOn, TimeOnly startsAt)
    {
        DateTime local = startsOn.ToDateTime(startsAt);

        return local < _clock.LocalNow.DateTime;
    }

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static bool TryParseTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            value.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    private static bool IsEmbeddingValid(NluEmbedding? embedding)
    {
        if (embedding is null || string.IsNullOrWhiteSpace(embedding.Model))
        {
            return false;
        }

        return embedding.Dim > 0
            && embedding.Values.Count == embedding.Dim
            && embedding.Values.All(double.IsFinite);
    }

    private static TurnIntent? ParseAction(string? action) =>
        Normalize(action) switch
        {
            "create_task" => TurnIntent.CreateTask,
            "create_reminder" => TurnIntent.CreateReminder,
            "create_meeting" => TurnIntent.CreateMeeting,
            "list_period" => TurnIntent.ListPeriod,
            "search" => TurnIntent.Search,
            "clarify" => TurnIntent.Clarify,
            "unknown" => TurnIntent.Unknown,
            _ => null,
        };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Interpretation(
        DraftKind? Kind,
        string? Description,
        DateOnly? StartsOn,
        TimeOnly? StartsAt,
        TimeSpan? Duration,
        bool IsDeadline,
        string? Question,
        bool NeedsClarification);
}
