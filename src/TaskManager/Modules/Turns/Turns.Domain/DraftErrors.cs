using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>Ожидаемые ошибки черновика.</summary>
public static class DraftErrors
{
    public const string ClarificationLimitExceededCode = "CLARIFICATION_LIMIT_EXCEEDED";

    public static DomainError IdRequired { get; } = DomainError.Expected(
        "DRAFT_ID_REQUIRED",
        "Идентификатор черновика обязателен.");

    public static DomainError TurnIdRequired { get; } = DomainError.Expected(
        "DRAFT_TURN_ID_REQUIRED",
        "Черновик обязан ссылаться на ход.");

    public static DomainError KindInvalid { get; } = DomainError.Expected(
        "DRAFT_KIND_INVALID",
        "Неизвестный тип черновика.");

    public static DomainError ClarificationRoundInvalid { get; } = DomainError.Expected(
        "DRAFT_CLARIFICATION_ROUND_INVALID",
        "Номер раунда уточнений выходит за допустимый диапазон.");

    public static DomainError ClarificationLimitExceeded { get; } = DomainError.Expected(
        ClarificationLimitExceededCode,
        "Превышен лимит раундов уточнений.");

    public static DomainError TimeWithoutDate { get; } = DomainError.Expected(
        "DRAFT_TIME_WITHOUT_DATE",
        "Время в черновике нельзя задать без даты.");

    public static DomainError DurationOnlyForMeeting { get; } = DomainError.Expected(
        "DRAFT_DURATION_ONLY_FOR_MEETING",
        "Длительность допустима только для встречи.");

    public static DomainError DurationRequiresTime { get; } = DomainError.Expected(
        "DRAFT_DURATION_REQUIRES_TIME",
        "Длительность задаётся только вместе со временем.");

    public static DomainError DurationNotPositive { get; } = DomainError.Expected(
        "DRAFT_DURATION_NOT_POSITIVE",
        "Длительность должна быть больше нуля.");

    public static DomainError IsDeadlineOnlyForTask { get; } = DomainError.Expected(
        "DRAFT_IS_DEADLINE_ONLY_FOR_TASK",
        "Признак дедлайна допустим только для задачи.");

    public static DomainError TimestampInvalid { get; } = DomainError.Expected(
        "DRAFT_TIMESTAMP_INVALID",
        "Время обновления черновика не может быть раньше времени создания.");
}
