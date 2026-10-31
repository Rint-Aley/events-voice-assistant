using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>Ожидаемые ошибки команды домена.</summary>
public static class DomainCommandErrors
{
    public static DomainError IdRequired { get; } = DomainError.Expected(
        "COMMAND_ID_REQUIRED",
        "Идентификатор команды обязателен.");

    public static DomainError TurnIdRequired { get; } = DomainError.Expected(
        "COMMAND_TURN_ID_REQUIRED",
        "Команда обязана ссылаться на ход.");

    public static DomainError SequenceInvalid { get; } = DomainError.Expected(
        "COMMAND_SEQ_INVALID",
        "Порядковый номер команды не может быть отрицательным.");

    public static DomainError OperationInvalid { get; } = DomainError.Expected(
        "COMMAND_OPERATION_INVALID",
        "Неизвестная операция команды.");

    public static DomainError PayloadRequired { get; } = DomainError.Expected(
        "COMMAND_PAYLOAD_REQUIRED",
        "Полезная нагрузка команды обязательна.");

    public static DomainError StatusInvalid { get; } = DomainError.Expected(
        "COMMAND_STATUS_INVALID",
        "Неизвестный статус команды.");

    public static DomainError AttemptsInvalid { get; } = DomainError.Expected(
        "COMMAND_ATTEMPTS_INVALID",
        "Количество попыток не может быть отрицательным.");

    public static DomainError MaxAttemptsInvalid { get; } = DomainError.Expected(
        "COMMAND_MAX_ATTEMPTS_INVALID",
        "Максимум попыток должен быть положительным.");

    public static DomainError StatusTransitionInvalid { get; } = DomainError.Expected(
        "COMMAND_STATUS_TRANSITION_INVALID",
        "Переход статуса команды недопустим.");

    public static DomainError TimestampInvalid { get; } = DomainError.Expected(
        "COMMAND_TIMESTAMP_INVALID",
        "Время изменения команды раньше времени создания.");
}
