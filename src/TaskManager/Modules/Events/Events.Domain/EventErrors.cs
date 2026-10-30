using TaskManager.SharedKernel;

namespace TaskManager.Events.Domain;

/// <summary>Ожидаемые ошибки создания и восстановления события.</summary>
public static class EventErrors
{
    public static DomainError IdRequired { get; } = DomainError.Expected(
        "EVENT_ID_REQUIRED",
        "Идентификатор события обязателен.");

    public static DomainError KindInvalid { get; } = DomainError.Expected(
        "KIND_INVALID",
        "Неизвестный тип события.");

    public static DomainError StatusInvalid { get; } = DomainError.Expected(
        "STATUS_INVALID",
        "Неизвестный статус события.");

    public static DomainError DescriptionRequired { get; } = DomainError.Expected(
        "DESCRIPTION_REQUIRED",
        "Заголовок обязателен для задачи, напоминания и встречи.");

    public static DomainError DateRequired { get; } = DomainError.Expected(
        "DATE_REQUIRED",
        "Для напоминания и встречи нужна дата.");

    public static DomainError TimeRequired { get; } = DomainError.Expected(
        "TIME_REQUIRED",
        "Для напоминания и встречи нужно время.");

    public static DomainError TimeWithoutDate { get; } = DomainError.Expected(
        "TIME_WITHOUT_DATE",
        "Время нельзя задать без даты.");

    public static DomainError DurationRequiresTime { get; } = DomainError.Expected(
        "DURATION_REQUIRES_TIME",
        "Длительность задаётся только вместе со временем.");

    public static DomainError DurationOnlyForMeeting { get; } = DomainError.Expected(
        "DURATION_ONLY_FOR_MEETING",
        "Длительность допустима только для встречи.");

    public static DomainError DurationNotPositive { get; } = DomainError.Expected(
        "DURATION_NOT_POSITIVE",
        "Длительность должна быть больше нуля.");

    public static DomainError IsDeadlineOnlyForTask { get; } = DomainError.Expected(
        "IS_DEADLINE_ONLY_FOR_TASK",
        "Признак дедлайна допустим только для задачи.");

    public static DomainError InvalidPeriod { get; } = DomainError.Expected(
        "INVALID_PERIOD",
        "Начало периода позже его конца.");
}
