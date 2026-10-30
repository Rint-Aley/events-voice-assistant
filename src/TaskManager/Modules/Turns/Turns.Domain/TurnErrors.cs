using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>Ожидаемые ошибки хода и его переходов.</summary>
public static class TurnErrors
{
    /// <summary>Код ошибки второго активного хода; совпадает с HTTP-кодом <c>SESSION_BUSY</c>.</summary>
    public const string SessionBusyCode = "SESSION_BUSY";

    public const string InvalidTransitionCode = "INVALID_TRANSITION";

    public const string StateVersionConflictCode = "STATE_VERSION_CONFLICT";

    public const string EmptyTranscriptCode = "EMPTY_TRANSCRIPT";

    public const string SttFailedCode = "STT_FAILED";

    public const string NluFailedCode = "NLU_FAILED";

    public const string DomainApplyFailedCode = "DOMAIN_APPLY_FAILED";

    public const string DeadlineExceededCode = "DEADLINE_EXCEEDED";

    /// <summary>Код неизвестного хода; совпадает с HTTP-кодом <c>TURN_NOT_FOUND</c>.</summary>
    public const string TurnNotFoundCode = "TURN_NOT_FOUND";

    public static DomainError IdRequired { get; } = DomainError.Expected(
        "TURN_ID_REQUIRED",
        "Идентификатор хода обязателен.");

    public static DomainError SessionIdRequired { get; } = DomainError.Expected(
        "SESSION_ID_REQUIRED",
        "Идентификатор сессии обязателен.");

    public static DomainError IdempotencyKeyRequired { get; } = DomainError.Expected(
        "IDEMPOTENCY_KEY_REQUIRED",
        "Ключ идемпотентности обязателен.");

    public static DomainError InitialStateInvalid { get; } = DomainError.Expected(
        "INITIAL_STATE_INVALID",
        "Новый ход может быть только в TRANSCRIBING или INTERPRETING.");

    public static DomainError StateInvalid { get; } = DomainError.Expected(
        "STATE_INVALID",
        "Неизвестное состояние хода.");

    public static DomainError StateVersionInvalid { get; } = DomainError.Expected(
        "STATE_VERSION_INVALID",
        "Версия состояния должна быть положительной.");

    public static DomainError StateVersionConflict { get; } = DomainError.Expected(
        StateVersionConflictCode,
        "Версия состояния устарела: переход уже сделан или выполняется параллельно.");

    public static DomainError InvalidTransition { get; } = DomainError.Expected(
        InvalidTransitionCode,
        "Переход из текущего состояния недопустим.");

    public static DomainError DeadlineInvalid { get; } = DomainError.Expected(
        "DEADLINE_INVALID",
        "Дедлайн хода должен быть в будущем.");

    public static DomainError TimestampInvalid { get; } = DomainError.Expected(
        "TIMESTAMP_INVALID",
        "Время обновления хода не может быть раньше времени создания.");

    public static DomainError TranscriptRequired { get; } = DomainError.Expected(
        "TRANSCRIPT_REQUIRED",
        "Текст хода не может быть пустым.");

    public static DomainError TextNotExpected { get; } = DomainError.Expected(
        "TEXT_NOT_EXPECTED",
        "Голосовой ход не содержит введённого текста.");

    public static DomainError AudioObjectKeyRequired { get; } = DomainError.Expected(
        "AUDIO_OBJECT_KEY_REQUIRED",
        "Голосовому ходу нужен ключ аудио в S3.");

    public static DomainError AudioObjectKeyNotExpected { get; } = DomainError.Expected(
        "AUDIO_OBJECT_KEY_NOT_EXPECTED",
        "Текстовому ходу не нужен ключ аудио.");

    public static DomainError SttConfidenceInvalid { get; } = DomainError.Expected(
        "STT_CONFIDENCE_INVALID",
        "Уверенность распознавания должна быть в диапазоне от 0 до 1.");

    public static DomainError DraftAlreadyExists { get; } = DomainError.Expected(
        "DRAFT_ALREADY_EXISTS",
        "У хода уже есть черновик.");

    public static DomainError DraftBelongsToAnotherTurn { get; } = DomainError.Expected(
        "DRAFT_BELONGS_TO_ANOTHER_TURN",
        "Черновик принадлежит другому ходу.");

    public static DomainError DraftRequiresClarificationState { get; } = DomainError.Expected(
        "DRAFT_REQUIRES_CLARIFICATION_STATE",
        "Черновик можно прикрепить только к ходу в NEEDS_CLARIFICATION.");

    public static DomainError SessionBusy { get; } = DomainError.Expected(
        SessionBusyCode,
        "В сессии уже есть активный ход.");

    public static DomainError TurnNotFound { get; } = DomainError.Expected(
        TurnNotFoundCode,
        "Ход не найден.");
}
