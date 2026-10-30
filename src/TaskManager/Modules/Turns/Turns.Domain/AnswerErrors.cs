using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>Ожидаемые ошибки ответа и его озвучки.</summary>
public static class AnswerErrors
{
    public static DomainError IdRequired { get; } = DomainError.Expected(
        "ANSWER_ID_REQUIRED",
        "Идентификатор ответа обязателен.");

    public static DomainError TurnIdRequired { get; } = DomainError.Expected(
        "ANSWER_TURN_ID_REQUIRED",
        "Ответ обязан ссылаться на ход.");

    public static DomainError TextRequired { get; } = DomainError.Expected(
        "ANSWER_TEXT_REQUIRED",
        "Текст ответа не может быть пустым.");

    public static DomainError TotalCountInvalid { get; } = DomainError.Expected(
        "ANSWER_TOTAL_COUNT_INVALID",
        "Количество записей не может быть отрицательным.");

    public static DomainError AudioStatusInvalid { get; } = DomainError.Expected(
        "ANSWER_AUDIO_STATUS_INVALID",
        "Неизвестный статус озвучки.");

    public static DomainError AudioStatusTransitionInvalid { get; } = DomainError.Expected(
        "ANSWER_AUDIO_STATUS_TRANSITION_INVALID",
        "Переход статуса озвучки недопустим.");

    public static DomainError AudioObjectKeyRequired { get; } = DomainError.Expected(
        "ANSWER_AUDIO_OBJECT_KEY_REQUIRED",
        "Для готовой озвучки нужен ключ объекта.");

    public static DomainError AudioDurationInvalid { get; } = DomainError.Expected(
        "ANSWER_AUDIO_DURATION_INVALID",
        "Длительность озвучки не может быть отрицательной.");

    public static DomainError AudioSynthesisFailed { get; } = DomainError.Expected(
        "ANSWER_AUDIO_SYNTHESIS_FAILED",
        "Не удалось синтезировать озвучку ответа.");

    public static DomainError NotFound { get; } = DomainError.Expected(
        "ANSWER_NOT_FOUND",
        "Ответ не найден.");
}
