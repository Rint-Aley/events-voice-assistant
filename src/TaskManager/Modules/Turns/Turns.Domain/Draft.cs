using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>
/// Недособранный запрос пользователя. Привязан к одному ходу (уникальность по <c>turn_id</c>)
/// и живёт, пока не хватает слотов. Невалидные дата и время в черновик не попадают.
/// Вектор эмбеддинга в домен не входит: <c>EMBEDDING_MODEL</c>/<c>EMBEDDING_DIM</c> —
/// <c>TBD(BLOCKING)</c>.
/// </summary>
public sealed class Draft
{
    /// <summary>Сколько раундов уточнений допускается до завершения хода.</summary>
    public const int MaxClarificationRounds = 2;

    private Draft(
        Guid id,
        Guid turnId,
        DraftKind? kind,
        string? description,
        string? sourceText,
        DateOnly? startsOn,
        TimeOnly? startsAt,
        TimeSpan? duration,
        bool isDeadline,
        int clarificationRound,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        TurnId = turnId;
        Kind = kind;
        Description = description;
        SourceText = sourceText;
        StartsOn = startsOn;
        StartsAt = startsAt;
        Duration = duration;
        IsDeadline = isDeadline;
        ClarificationRound = clarificationRound;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Идентификатор черновика.</summary>
    public Guid Id { get; }

    /// <summary>Ход, которому принадлежит черновик; значение уникально.</summary>
    public Guid TurnId { get; }

    /// <summary>Тип записи, если <c>nlu</c> его уже определил.</summary>
    public DraftKind? Kind { get; private set; }

    /// <summary>Заголовок, если он уже собран.</summary>
    public string? Description { get; private set; }

    /// <summary>Исходные фразы пользователя через <c>; </c>.</summary>
    public string? SourceText { get; private set; }

    /// <summary>Локальная дата в <c>TIMEZONE</c>.</summary>
    public DateOnly? StartsOn { get; private set; }

    /// <summary>Локальное время в <c>TIMEZONE</c>; без даты не задаётся.</summary>
    public TimeOnly? StartsAt { get; private set; }

    /// <summary>Длительность встречи.</summary>
    public TimeSpan? Duration { get; private set; }

    /// <summary>Признак дедлайна; допустим только для задачи.</summary>
    public bool IsDeadline { get; private set; }

    /// <summary>Сколько раундов уточнений уже задано.</summary>
    public int ClarificationRound { get; private set; }

    /// <summary>Остался ли ещё хотя бы один раунд уточнений.</summary>
    public bool CanAskClarification => ClarificationRound < MaxClarificationRounds;

    /// <summary>Время создания черновика.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Время последнего изменения черновика.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Создаёт новый черновик с нулевым числом раундов уточнений.</summary>
    public static Result<Draft> Create(
        Guid turnId,
        DateTimeOffset now,
        DraftKind? kind = null,
        string? description = null,
        string? sourceText = null,
        DateOnly? startsOn = null,
        TimeOnly? startsAt = null,
        TimeSpan? duration = null,
        bool isDeadline = false,
        Guid? id = null)
    {
        return Build(
            id ?? Guid.NewGuid(),
            turnId,
            kind,
            description,
            sourceText,
            startsOn,
            startsAt,
            duration,
            isDeadline,
            clarificationRound: 0,
            createdAt: now,
            updatedAt: now);
    }

    /// <summary>Восстанавливает уже сохранённый черновик из хранилища.</summary>
    public static Result<Draft> Restore(
        Guid id,
        Guid turnId,
        int clarificationRound,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DraftKind? kind = null,
        string? description = null,
        string? sourceText = null,
        DateOnly? startsOn = null,
        TimeOnly? startsAt = null,
        TimeSpan? duration = null,
        bool isDeadline = false)
    {
        return Build(
            id,
            turnId,
            kind,
            description,
            sourceText,
            startsOn,
            startsAt,
            duration,
            isDeadline,
            clarificationRound,
            createdAt,
            updatedAt);
    }

    /// <summary>
    /// Дополняет <see cref="SourceText"/> исходной фразой пользователя через <c>; </c>.
    /// Пустая фраза игнорируется.
    /// </summary>
    public Result AddSourceText(string? sourceText, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return Result.Success();
        }

        var phrase = sourceText.Trim();
        SourceText = string.IsNullOrWhiteSpace(SourceText)
            ? phrase
            : $"{SourceText}; {phrase}";
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Заменяет слоты черновика целиком. Слияние новых значений с уже собранными выполняет
    /// сценарий, домен проверяет итоговый набор слотов; невалидный набор не применяется.
    /// </summary>
    public Result UpdateSlots(
        DraftKind? kind,
        string? description,
        DateOnly? startsOn,
        TimeOnly? startsAt,
        TimeSpan? duration,
        bool isDeadline,
        DateTimeOffset now)
    {
        var error = ValidateSlots(kind, startsOn, startsAt, duration, isDeadline);
        if (error is not null)
        {
            return Result.Failure(error);
        }

        Kind = kind;
        Description = Normalize(description);
        StartsOn = startsOn;
        StartsAt = startsAt;
        Duration = duration;
        IsDeadline = isDeadline;
        UpdatedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Начинает очередной раунд уточнений. <see cref="ClarificationRound"/> считает уже
    /// заданные вопросы: два вопроса допустимы, третий превышает лимит, после чего сценарий
    /// завершает ход и удаляет черновик.
    /// </summary>
    public Result BeginClarificationRound(DateTimeOffset now)
    {
        if (!CanAskClarification)
        {
            return Result.Failure(DraftErrors.ClarificationLimitExceeded);
        }

        ClarificationRound++;
        UpdatedAt = now;

        return Result.Success();
    }

    private static Result<Draft> Build(
        Guid id,
        Guid turnId,
        DraftKind? kind,
        string? description,
        string? sourceText,
        DateOnly? startsOn,
        TimeOnly? startsAt,
        TimeSpan? duration,
        bool isDeadline,
        int clarificationRound,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            return Result<Draft>.Failure(DraftErrors.IdRequired);
        }

        if (turnId == Guid.Empty)
        {
            return Result<Draft>.Failure(DraftErrors.TurnIdRequired);
        }

        if (kind.HasValue && !Enum.IsDefined(kind.Value))
        {
            return Result<Draft>.Failure(DraftErrors.KindInvalid);
        }

        if (clarificationRound is < 0 or > MaxClarificationRounds)
        {
            return Result<Draft>.Failure(DraftErrors.ClarificationRoundInvalid);
        }

        if (updatedAt < createdAt)
        {
            return Result<Draft>.Failure(DraftErrors.TimestampInvalid);
        }

        var slotError = ValidateSlots(kind, startsOn, startsAt, duration, isDeadline);
        if (slotError is not null)
        {
            return Result<Draft>.Failure(slotError);
        }

        return Result<Draft>.Success(new Draft(
            id,
            turnId,
            kind,
            Normalize(description),
            Normalize(sourceText),
            startsOn,
            startsAt,
            duration,
            isDeadline,
            clarificationRound,
            createdAt,
            updatedAt));
    }

    private static DomainError? ValidateSlots(
        DraftKind? kind,
        DateOnly? startsOn,
        TimeOnly? startsAt,
        TimeSpan? duration,
        bool isDeadline)
    {
        if (startsAt.HasValue && !startsOn.HasValue)
        {
            return DraftErrors.TimeWithoutDate;
        }

        if (isDeadline && kind != DraftKind.Task)
        {
            return DraftErrors.IsDeadlineOnlyForTask;
        }

        if (duration.HasValue)
        {
            if (kind != DraftKind.Meeting)
            {
                return DraftErrors.DurationOnlyForMeeting;
            }

            if (!startsAt.HasValue)
            {
                return DraftErrors.DurationRequiresTime;
            }

            if (duration.Value <= TimeSpan.Zero)
            {
                return DraftErrors.DurationNotPositive;
            }
        }

        return null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
