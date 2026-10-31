namespace TaskManager.Turns.Domain;

/// <summary>
/// Состояние хода. Значения перечислены в порядке, принятом в <c>turns.state</c>:
/// <c>TRANSCRIBING</c>, <c>INTERPRETING</c>, <c>APPLYING</c>, <c>COMPLETED</c>,
/// <c>NEEDS_CLARIFICATION</c>, <c>FAILED</c>, <c>EXPIRED</c>.
/// </summary>
public enum TurnState
{
    /// <summary>Ждём ответ <c>stt</c>.</summary>
    Transcribing,

    /// <summary>Ждём ответ <c>nlu</c>.</summary>
    Interpreting,

    /// <summary>Ждём применения <c>domain_commands</c>.</summary>
    Applying,

    /// <summary>Ответ готов; <c>tts</c> может дописать аудио.</summary>
    Completed,

    /// <summary>Ход закончился вопросом, черновик сохранён.</summary>
    NeedsClarification,

    /// <summary>Обработка не удалась.</summary>
    Failed,

    /// <summary>Дедлайн истёк.</summary>
    Expired,
}
