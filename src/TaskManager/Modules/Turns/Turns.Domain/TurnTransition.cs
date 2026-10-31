namespace TaskManager.Turns.Domain;

/// <summary>
/// Событие, которое двигает ход по таблице переходов <c>task-manager.md</c> §4.2.
/// </summary>
public enum TurnTransition
{
    /// <summary>Ответ <c>stt</c> получен: <c>TRANSCRIBING</c> → <c>INTERPRETING</c>.</summary>
    SttResult,

    /// <summary>В записи тишина: <c>TRANSCRIBING</c> → <c>FAILED</c>.</summary>
    EmptyTranscript,

    /// <summary>Попытки <c>stt</c> исчерпаны: <c>TRANSCRIBING</c> → <c>FAILED</c>.</summary>
    SttFailed,

    /// <summary>План <c>nlu</c> — чтение: <c>INTERPRETING</c> → <c>COMPLETED</c>.</summary>
    ReadPlan,

    /// <summary>Домен вернул ошибку валидации: <c>INTERPRETING</c> → <c>NEEDS_CLARIFICATION</c>.</summary>
    DomainValidationFailed,

    /// <summary>План <c>nlu</c> — запись: <c>INTERPRETING</c> → <c>APPLYING</c>.</summary>
    WritePlan,

    /// <summary>План <c>nlu</c> — уточнение: <c>INTERPRETING</c> → <c>NEEDS_CLARIFICATION</c>.</summary>
    ClarifyPlan,

    /// <summary>План <c>nlu</c> — не понял: <c>INTERPRETING</c> → <c>COMPLETED</c>.</summary>
    NotUnderstood,

    /// <summary>Попытки <c>nlu</c> исчерпаны: <c>INTERPRETING</c> → <c>FAILED</c>.</summary>
    NluFailed,

    /// <summary>Команды применены: <c>APPLYING</c> → <c>COMPLETED</c>.</summary>
    CommandsApplied,

    /// <summary>Попытки применения исчерпаны: <c>APPLYING</c> → <c>FAILED</c>.</summary>
    ApplyFailed,

    /// <summary>
    /// Дедлайн прошёл: любой нетерминальный ход → <c>EXPIRED</c>. Событие поднимает
    /// <c>reconciler</c> после проверки <c>deadline_at</c>.
    /// </summary>
    DeadlineExceeded,
}
