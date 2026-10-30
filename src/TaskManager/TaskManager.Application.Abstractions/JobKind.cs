namespace TaskManager.Application.Abstractions;

/// <summary>
/// Задача, которую Application ставит планировщику. Периодические задачи
/// <c>apply</c> и <c>reconciler</c> регистрируются адаптером и сюда не входят.
/// </summary>
public enum JobKind
{
    /// <summary>Распознавание речи, ключ <c>stt:{turn_id}</c>, цель — ход.</summary>
    Stt,

    /// <summary>Разбор текста, ключ <c>nlu:{turn_id}</c>, цель — ход.</summary>
    Nlu,

    /// <summary>Синтез ответа, ключ <c>tts:{answer_id}</c>, цель — ответ.</summary>
    Tts,
}
