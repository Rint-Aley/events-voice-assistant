using TaskManager.SharedKernel;

namespace TaskManager.Events.Domain;

/// <summary>Запись пользователя: задача, напоминание или встреча.</summary>
public sealed class Event
{
    /// <summary>Длительность встречи по умолчанию.</summary>
    public static readonly TimeSpan DefaultMeetingDuration = TimeSpan.FromMinutes(60);

    private Event()
    {
        Description = null!;
    }

    private Event(
        Guid id,
        EventKind kind,
        EventStatus status,
        string description,
        string? sourceText,
        DateOnly? startsOn,
        TimeOnly? startsAt,
        TimeSpan? duration,
        bool isDeadline)
    {
        Id = id;
        Kind = kind;
        Status = status;
        Description = description;
        SourceText = sourceText;
        StartsOn = startsOn;
        StartsAt = startsAt;
        Duration = duration;
        IsDeadline = isDeadline;
    }

    /// <summary>Идентификатор записи.</summary>
    public Guid Id { get; }

    /// <summary>Тип записи.</summary>
    public EventKind Kind { get; }

    /// <summary>Статус записи. При создании всегда <see cref="EventStatus.Active"/>.</summary>
    public EventStatus Status { get; }

    /// <summary>Обязательный заголовок.</summary>
    public string Description { get; }

    /// <summary>Исходные фразы пользователя; в эмбеддинге не участвуют.</summary>
    public string? SourceText { get; }

    /// <summary>Локальная дата в <c>TIMEZONE</c>.</summary>
    public DateOnly? StartsOn { get; }

    /// <summary>Локальное время в <c>TIMEZONE</c>; без даты не задаётся.</summary>
    public TimeOnly? StartsAt { get; }

    /// <summary>Длительность встречи; для остальных типов не задаётся.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>Признак дедлайна; допустим только для задачи.</summary>
    public bool IsDeadline { get; }

    /// <summary>Запись участвует в поиске и списке.</summary>
    public bool IsVisible => Status is EventStatus.Active or EventStatus.Done;

    /// <summary>Начало в локальном времени, если заданы дата и время.</summary>
    public DateTime? Start => StartsOn is { } date && StartsAt is { } time
        ? date.ToDateTime(time)
        : null;

    /// <summary>Конец в локальном времени; конец интервала не входит в саму встречу.</summary>
    public DateTime? End => Start is { } start && Duration is { } duration
        ? start + duration
        : null;

    /// <summary>
    /// Создаёт новую запись в статусе <see cref="EventStatus.Active"/>.
    /// </summary>
    /// <param name="kind">Тип записи.</param>
    /// <param name="description">Обязательный заголовок.</param>
    /// <param name="sourceText">Исходные фразы пользователя.</param>
    /// <param name="startsOn">Локальная дата.</param>
    /// <param name="startsAt">Локальное время; требует даты.</param>
    /// <param name="duration">Длительность встречи; по умолчанию 60 минут.</param>
    /// <param name="isDeadline">Признак дедлайна; только для задачи.</param>
    /// <param name="id">Идентификатор; при отсутствии генерируется новый.</param>
    public static Result<Event> Create(
        EventKind kind,
        string? description,
        string? sourceText = null,
        DateOnly? startsOn = null,
        TimeOnly? startsAt = null,
        TimeSpan? duration = null,
        bool isDeadline = false,
        Guid? id = null)
    {
        return Build(
            id ?? Guid.NewGuid(),
            kind,
            EventStatus.Active,
            description,
            sourceText,
            startsOn,
            startsAt,
            duration,
            isDeadline);
    }

    /// <summary>
    /// Восстанавливает существующую запись из хранилища. Переводы статусов в MVP не
    /// реализуются: метод нужен только для загрузки уже сохранённого события.
    /// </summary>
    public static Result<Event> Restore(
        Guid id,
        EventKind kind,
        EventStatus status,
        string? description,
        string? sourceText = null,
        DateOnly? startsOn = null,
        TimeOnly? startsAt = null,
        TimeSpan? duration = null,
        bool isDeadline = false)
    {
        return Build(
            id,
            kind,
            status,
            description,
            sourceText,
            startsOn,
            startsAt,
            duration,
            isDeadline);
    }

    private static Result<Event> Build(
        Guid id,
        EventKind kind,
        EventStatus status,
        string? description,
        string? sourceText,
        DateOnly? startsOn,
        TimeOnly? startsAt,
        TimeSpan? duration,
        bool isDeadline)
    {
        if (id == Guid.Empty)
        {
            return Result<Event>.Failure(EventErrors.IdRequired);
        }

        if (!Enum.IsDefined(kind))
        {
            return Result<Event>.Failure(EventErrors.KindInvalid);
        }

        if (!Enum.IsDefined(status))
        {
            return Result<Event>.Failure(EventErrors.StatusInvalid);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result<Event>.Failure(EventErrors.DescriptionRequired);
        }

        if (kind is EventKind.Reminder or EventKind.Meeting)
        {
            if (!startsOn.HasValue)
            {
                return Result<Event>.Failure(EventErrors.DateRequired);
            }

            if (!startsAt.HasValue)
            {
                return Result<Event>.Failure(EventErrors.TimeRequired);
            }
        }

        if (startsAt.HasValue && !startsOn.HasValue)
        {
            return Result<Event>.Failure(EventErrors.TimeWithoutDate);
        }

        if (isDeadline && kind != EventKind.Task)
        {
            return Result<Event>.Failure(EventErrors.IsDeadlineOnlyForTask);
        }

        if (duration.HasValue)
        {
            if (kind != EventKind.Meeting)
            {
                return Result<Event>.Failure(EventErrors.DurationOnlyForMeeting);
            }

            if (!startsAt.HasValue)
            {
                return Result<Event>.Failure(EventErrors.DurationRequiresTime);
            }

            if (duration.Value <= TimeSpan.Zero)
            {
                return Result<Event>.Failure(EventErrors.DurationNotPositive);
            }
        }
        else if (kind == EventKind.Meeting)
        {
            duration = DefaultMeetingDuration;
        }

        return Result<Event>.Success(new Event(
            id,
            kind,
            status,
            description.Trim(),
            string.IsNullOrWhiteSpace(sourceText) ? null : sourceText.Trim(),
            startsOn,
            startsAt,
            duration,
            isDeadline));
    }
}
