using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>Ожидаемые ошибки связи ответа с событием.</summary>
public static class AnswerEventErrors
{
    public static DomainError AnswerIdRequired { get; } = DomainError.Expected(
        "ANSWER_EVENT_ANSWER_ID_REQUIRED",
        "Связь обязана ссылаться на ответ.");

    public static DomainError EventIdRequired { get; } = DomainError.Expected(
        "ANSWER_EVENT_EVENT_ID_REQUIRED",
        "Связь обязана ссылаться на событие.");

    public static DomainError PositionInvalid { get; } = DomainError.Expected(
        "ANSWER_EVENT_POSITION_INVALID",
        "Позиция события в ответе не может быть отрицательной.");
}
