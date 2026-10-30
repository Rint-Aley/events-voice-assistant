using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>
/// Один цикл «запрос → ответ». Состояние меняется только по таблице переходов, каждый переход
/// проверяет <see cref="StateVersion"/> (CAS), а терминальные состояния не имеют исходящих
/// переходов.
/// </summary>
public sealed class Turn
{
    /// <summary>Версия только что созданного хода.</summary>
    public const int InitialStateVersion = 1;

    private Turn(
        Guid id,
        Guid sessionId,
        TurnState state,
        int stateVersion,
        Guid idempotencyKey,
        string? text,
        string? audioObjectKey,
        decimal? sttConfidence,
        string? plan,
        string? errorCode,
        DateTimeOffset deadlineAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        Draft? draft)
    {
        Id = id;
        SessionId = sessionId;
        State = state;
        StateVersion = stateVersion;
        IdempotencyKey = idempotencyKey;
        Text = text;
        AudioObjectKey = audioObjectKey;
        SttConfidence = sttConfidence;
        Plan = plan;
        ErrorCode = errorCode;
        DeadlineAt = deadlineAt;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Draft = draft;
    }

    /// <summary>Идентификатор хода.</summary>
    public Guid Id { get; }

    /// <summary>Сессия, в которой существует ход; активный ход может быть только один.</summary>
    public Guid SessionId { get; }

    /// <summary>Текущее состояние.</summary>
    public TurnState State { get; private set; }

    /// <summary>Версия состояния для CAS; увеличивается на каждом переходе.</summary>
    public int StateVersion { get; private set; }

    /// <summary>Ключ идемпотентности от клиента.</summary>
    public Guid IdempotencyKey { get; }

    /// <summary>Введённый текст или результат распознавания.</summary>
    public string? Text { get; private set; }

    /// <summary>Ключ аудио в S3; сам файл в базе не хранится.</summary>
    public string? AudioObjectKey { get; }

    /// <summary>Уверенность распознавания от 0 до 1.</summary>
    public decimal? SttConfidence { get; private set; }

    /// <summary>Сырой план <c>nlu</c>; домен его не интерпретирует.</summary>
    public string? Plan { get; private set; }

    /// <summary>Код ошибки обработки; заполняется только у <see cref="TurnState.Failed"/> и <see cref="TurnState.Expired"/>.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>Момент, после которого ход закрывается как <see cref="TurnState.Expired"/>.</summary>
    public DateTimeOffset DeadlineAt { get; }

    /// <summary>Время создания хода.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Время последнего перехода.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Черновик хода; не больше одного на ход.</summary>
    public Draft? Draft { get; private set; }

    /// <summary>Ход ещё обрабатывается и занимает сессию.</summary>
    public bool IsActive => TurnStateMachine.IsActive(State);

    /// <summary>Ход завершён и больше не меняет состояние.</summary>
    public bool IsTerminal => TurnStateMachine.IsTerminal(State);

    /// <summary>
    /// Создаёт голосовой ход в <see cref="TurnState.Transcribing"/> или текстовый ход в
    /// <see cref="TurnState.Interpreting"/>. Голосовому ходу нужен ключ аудио, текстовому —
    /// введённый текст.
    /// </summary>
    public static Result<Turn> Create(
        Guid sessionId,
        TurnState initialState,
        Guid idempotencyKey,
        DateTimeOffset deadlineAt,
        DateTimeOffset now,
        Guid? id = null,
        string? audioObjectKey = null,
        string? text = null)
    {
        if (sessionId == Guid.Empty)
        {
            return Result<Turn>.Failure(TurnErrors.SessionIdRequired);
        }

        if (idempotencyKey == Guid.Empty)
        {
            return Result<Turn>.Failure(TurnErrors.IdempotencyKeyRequired);
        }

        if (initialState is not (TurnState.Transcribing or TurnState.Interpreting))
        {
            return Result<Turn>.Failure(TurnErrors.InitialStateInvalid);
        }

        if (deadlineAt <= now)
        {
            return Result<Turn>.Failure(TurnErrors.DeadlineInvalid);
        }

        var normalizedText = Normalize(text);
        var normalizedAudioObjectKey = Normalize(audioObjectKey);

        if (initialState == TurnState.Transcribing)
        {
            if (normalizedAudioObjectKey is null)
            {
                return Result<Turn>.Failure(TurnErrors.AudioObjectKeyRequired);
            }

            if (normalizedText is not null)
            {
                return Result<Turn>.Failure(TurnErrors.TextNotExpected);
            }
        }
        else
        {
            if (normalizedText is null)
            {
                return Result<Turn>.Failure(TurnErrors.TranscriptRequired);
            }

            if (normalizedAudioObjectKey is not null)
            {
                return Result<Turn>.Failure(TurnErrors.AudioObjectKeyNotExpected);
            }
        }

        var turnId = id ?? Guid.NewGuid();
        if (turnId == Guid.Empty)
        {
            return Result<Turn>.Failure(TurnErrors.IdRequired);
        }

        return Result<Turn>.Success(new Turn(
            turnId,
            sessionId,
            initialState,
            InitialStateVersion,
            idempotencyKey,
            normalizedText,
            normalizedAudioObjectKey,
            sttConfidence: null,
            plan: null,
            errorCode: null,
            deadlineAt,
            createdAt: now,
            updatedAt: now,
            draft: null));
    }

    /// <summary>Восстанавливает сохранённый ход из хранилища без проверки переходов.</summary>
    public static Result<Turn> Restore(
        Guid id,
        Guid sessionId,
        TurnState state,
        int stateVersion,
        Guid idempotencyKey,
        DateTimeOffset deadlineAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string? text = null,
        string? audioObjectKey = null,
        decimal? sttConfidence = null,
        string? plan = null,
        string? errorCode = null,
        Draft? draft = null)
    {
        if (id == Guid.Empty)
        {
            return Result<Turn>.Failure(TurnErrors.IdRequired);
        }

        if (sessionId == Guid.Empty)
        {
            return Result<Turn>.Failure(TurnErrors.SessionIdRequired);
        }

        if (idempotencyKey == Guid.Empty)
        {
            return Result<Turn>.Failure(TurnErrors.IdempotencyKeyRequired);
        }

        if (!Enum.IsDefined(state))
        {
            return Result<Turn>.Failure(TurnErrors.StateInvalid);
        }

        if (stateVersion < InitialStateVersion)
        {
            return Result<Turn>.Failure(TurnErrors.StateVersionInvalid);
        }

        if (updatedAt < createdAt)
        {
            return Result<Turn>.Failure(TurnErrors.TimestampInvalid);
        }

        if (sttConfidence is < 0m or > 1m)
        {
            return Result<Turn>.Failure(TurnErrors.SttConfidenceInvalid);
        }

        if (draft is not null)
        {
            if (draft.TurnId != id)
            {
                return Result<Turn>.Failure(TurnErrors.DraftBelongsToAnotherTurn);
            }

            if (state != TurnState.NeedsClarification)
            {
                return Result<Turn>.Failure(TurnErrors.DraftRequiresClarificationState);
            }
        }

        return Result<Turn>.Success(new Turn(
            id,
            sessionId,
            state,
            stateVersion,
            idempotencyKey,
            Normalize(text),
            Normalize(audioObjectKey),
            sttConfidence,
            Normalize(plan),
            Normalize(errorCode),
            deadlineAt,
            createdAt,
            updatedAt,
            draft));
    }

    /// <summary>
    /// Выполняет переход по таблице состояний с проверкой ожидаемой версии. При устаревшей
    /// версии возвращается <see cref="TurnErrors.StateVersionConflict"/>, при недопустимом
    /// событии — <see cref="TurnErrors.InvalidTransition"/>; состояние не меняется.
    /// </summary>
    public Result<TurnState> Transition(
        TurnTransition transition,
        int expectedStateVersion,
        DateTimeOffset now)
    {
        // CAS проверяется первым: устаревший результат отбрасывается до разбора события.
        if (expectedStateVersion != StateVersion)
        {
            return Result<TurnState>.Failure(TurnErrors.StateVersionConflict);
        }

        var next = TurnStateMachine.Next(State, transition);
        if (next.IsFailure)
        {
            return next;
        }

        State = next.Value;
        StateVersion++;
        UpdatedAt = now;
        ErrorCode = TurnStateMachine.ErrorCodeFor(transition);

        return next;
    }

    /// <summary>
    /// Сохраняет результат <c>stt</c> и переводит ход в <see cref="TurnState.Interpreting"/>.
    /// Пустой текст — отдельный переход <see cref="TurnTransition.EmptyTranscript"/>.
    /// </summary>
    public Result ApplySttResult(
        string? text,
        decimal? confidence,
        int expectedStateVersion,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Failure(TurnErrors.TranscriptRequired);
        }

        if (confidence is < 0m or > 1m)
        {
            return Result.Failure(TurnErrors.SttConfidenceInvalid);
        }

        var transition = Transition(TurnTransition.SttResult, expectedStateVersion, now);
        if (transition.IsFailure)
        {
            return transition;
        }

        Text = text.Trim();
        SttConfidence = confidence;

        return Result.Success();
    }

    /// <summary>
    /// Выполняет переход из <see cref="TurnState.Interpreting"/> и сохраняет сырой план
    /// <c>nlu</c> для отладки.
    /// </summary>
    public Result ApplyPlan(
        TurnTransition transition,
        string? plan,
        int expectedStateVersion,
        DateTimeOffset now)
    {
        if (transition is not (
            TurnTransition.ReadPlan
            or TurnTransition.DomainValidationFailed
            or TurnTransition.WritePlan
            or TurnTransition.ClarifyPlan
            or TurnTransition.NotUnderstood))
        {
            return Result.Failure(TurnErrors.InvalidTransition);
        }

        var result = Transition(transition, expectedStateVersion, now);
        if (result.IsFailure)
        {
            return result;
        }

        Plan = Normalize(plan);

        return Result.Success();
    }

    /// <summary>
    /// Прикрепляет черновик к ходу. Черновик уникален по <c>turn_id</c> и допустим только
    /// в <see cref="TurnState.NeedsClarification"/>.
    /// </summary>
    public Result AttachDraft(Draft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.TurnId != Id)
        {
            return Result.Failure(TurnErrors.DraftBelongsToAnotherTurn);
        }

        if (Draft is not null)
        {
            return Result.Failure(TurnErrors.DraftAlreadyExists);
        }

        if (State != TurnState.NeedsClarification)
        {
            return Result.Failure(TurnErrors.DraftRequiresClarificationState);
        }

        Draft = draft;

        return Result.Success();
    }

    /// <summary>
    /// Удаляет черновик хода. Отмена идемпотентна: повторный вызов и отсутствие черновика
    /// не являются ошибкой. Ход при этом остаётся в прежнем состоянии.
    /// </summary>
    public Result CancelDraft()
    {
        Draft = null;

        return Result.Success();
    }

    /// <summary>
    /// Сбрасывает черновик, если интент не продолжает сборку (<c>create_*</c> и
    /// <c>clarify</c> черновик сохраняют). Возвращает <see langword="true"/>, если черновик
    /// был удалён.
    /// </summary>
    public bool TryResetDraftFor(TurnIntent intent)
    {
        if (Draft is null || !DraftPolicy.ShouldReset(intent))
        {
            return false;
        }

        Draft = null;

        return true;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
