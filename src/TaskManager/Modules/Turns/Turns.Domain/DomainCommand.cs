using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>
/// Намерение записи, которое применяется отдельной транзакцией. Несколько команд одного хода
/// применяются по возрастанию <see cref="Seq"/>; содержимое <see cref="Payload"/> домен не
/// интерпретирует.
/// </summary>
public sealed class DomainCommand
{
    /// <summary>Сколько попыток применения допускается по умолчанию.</summary>
    public const int DefaultMaxAttempts = 3;

    private DomainCommand()
    {
        Payload = null!;
    }

    private DomainCommand(
        Guid id,
        Guid turnId,
        short seq,
        DomainCommandOperation operation,
        string payload,
        DomainCommandStatus status,
        int attempts,
        DateTimeOffset nextAttemptAt,
        string? lastError,
        DateTimeOffset createdAt,
        DateTimeOffset? appliedAt)
    {
        Id = id;
        TurnId = turnId;
        Seq = seq;
        Operation = operation;
        Payload = payload;
        Status = status;
        Attempts = attempts;
        NextAttemptAt = nextAttemptAt;
        LastError = lastError;
        CreatedAt = createdAt;
        AppliedAt = appliedAt;
    }

    /// <summary>Идентификатор команды.</summary>
    public Guid Id { get; }

    /// <summary>Ход, который создал команду.</summary>
    public Guid TurnId { get; }

    /// <summary>Порядок применения внутри хода.</summary>
    public short Seq { get; }

    /// <summary>Операция над доменом.</summary>
    public DomainCommandOperation Operation { get; }

    /// <summary>Непрозрачная полезная нагрузка команды.</summary>
    public string Payload { get; }

    /// <summary>Текущий статус применения.</summary>
    public DomainCommandStatus Status { get; private set; }

    /// <summary>Сколько раз команда уже пыталась примениться.</summary>
    public int Attempts { get; private set; }

    /// <summary>Когда команду можно попробовать применить снова.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>Текст последней ошибки применения.</summary>
    public string? LastError { get; private set; }

    /// <summary>Время создания команды.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Время успешного применения; <see langword="null"/>, если ещё не применена.</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    /// <summary>Команду пора применять.</summary>
    public bool IsDue(DateTimeOffset now) =>
        Status == DomainCommandStatus.Pending && NextAttemptAt <= now;

    /// <summary>Создаёт команду в статусе <see cref="DomainCommandStatus.Pending"/>.</summary>
    public static Result<DomainCommand> Create(
        Guid turnId,
        short seq,
        DomainCommandOperation operation,
        string? payload,
        DateTimeOffset now,
        Guid? id = null)
    {
        return Build(
            id ?? Guid.NewGuid(),
            turnId,
            seq,
            operation,
            payload,
            DomainCommandStatus.Pending,
            attempts: 0,
            nextAttemptAt: now,
            lastError: null,
            createdAt: now,
            appliedAt: null);
    }

    /// <summary>Восстанавливает сохранённую команду из хранилища.</summary>
    public static Result<DomainCommand> Restore(
        Guid id,
        Guid turnId,
        short seq,
        DomainCommandOperation operation,
        string? payload,
        DomainCommandStatus status,
        int attempts,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset createdAt,
        string? lastError = null,
        DateTimeOffset? appliedAt = null)
    {
        return Build(
            id,
            turnId,
            seq,
            operation,
            payload,
            status,
            attempts,
            nextAttemptAt,
            lastError,
            createdAt,
            appliedAt);
    }

    /// <summary>Помечает команду применённой. Повторное применение идемпотентно.</summary>
    public Result MarkApplied(DateTimeOffset now)
    {
        if (Status == DomainCommandStatus.Applied)
        {
            return Result.Success();
        }

        if (Status != DomainCommandStatus.Pending)
        {
            return Result.Failure(DomainCommandErrors.StatusTransitionInvalid);
        }

        Status = DomainCommandStatus.Applied;
        AppliedAt = now;

        return Result.Success();
    }

    /// <summary>
    /// Регистрирует неудачную попытку: увеличивает счётчик и назначает следующий запуск.
    /// После <paramref name="maxAttempts"/> попыток команда становится
    /// <see cref="DomainCommandStatus.Failed"/>.
    /// </summary>
    public Result RegisterFailure(
        string? error,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset now,
        int maxAttempts = DefaultMaxAttempts)
    {
        if (maxAttempts <= 0)
        {
            return Result.Failure(DomainCommandErrors.MaxAttemptsInvalid);
        }

        if (Status != DomainCommandStatus.Pending)
        {
            return Result.Failure(DomainCommandErrors.StatusTransitionInvalid);
        }

        Attempts++;
        LastError = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
        NextAttemptAt = nextAttemptAt;

        if (Attempts >= maxAttempts)
        {
            Status = DomainCommandStatus.Failed;
        }

        return Result.Success();
    }

    private static Result<DomainCommand> Build(
        Guid id,
        Guid turnId,
        short seq,
        DomainCommandOperation operation,
        string? payload,
        DomainCommandStatus status,
        int attempts,
        DateTimeOffset nextAttemptAt,
        string? lastError,
        DateTimeOffset createdAt,
        DateTimeOffset? appliedAt)
    {
        if (id == Guid.Empty)
        {
            return Result<DomainCommand>.Failure(DomainCommandErrors.IdRequired);
        }

        if (turnId == Guid.Empty)
        {
            return Result<DomainCommand>.Failure(DomainCommandErrors.TurnIdRequired);
        }

        if (seq < 0)
        {
            return Result<DomainCommand>.Failure(DomainCommandErrors.SequenceInvalid);
        }

        if (!Enum.IsDefined(operation))
        {
            return Result<DomainCommand>.Failure(DomainCommandErrors.OperationInvalid);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result<DomainCommand>.Failure(DomainCommandErrors.PayloadRequired);
        }

        if (!Enum.IsDefined(status))
        {
            return Result<DomainCommand>.Failure(DomainCommandErrors.StatusInvalid);
        }

        if (attempts < 0)
        {
            return Result<DomainCommand>.Failure(DomainCommandErrors.AttemptsInvalid);
        }

        if (appliedAt.HasValue && appliedAt.Value < createdAt)
        {
            return Result<DomainCommand>.Failure(DomainCommandErrors.TimestampInvalid);
        }

        return Result<DomainCommand>.Success(new DomainCommand(
            id,
            turnId,
            seq,
            operation,
            payload.Trim(),
            status,
            attempts,
            nextAttemptAt,
            string.IsNullOrWhiteSpace(lastError) ? null : lastError.Trim(),
            createdAt,
            appliedAt));
    }
}
