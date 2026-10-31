namespace TaskManager.Turns.Domain;

/// <summary>
/// Намерение, которое <c>nlu</c> вернул в <c>action</c>. Новые значения не добавляются:
/// список соответствует <c>docs/contracts/nlu.response.json</c>.
/// </summary>
public enum TurnIntent
{
    /// <summary>Создать задачу.</summary>
    CreateTask,

    /// <summary>Создать напоминание.</summary>
    CreateReminder,

    /// <summary>Создать встречу.</summary>
    CreateMeeting,

    /// <summary>Выборка за период.</summary>
    ListPeriod,

    /// <summary>Поиск по смыслу.</summary>
    Search,

    /// <summary>Уточнение незавершённого запроса.</summary>
    Clarify,

    /// <summary>Намерение не распознано.</summary>
    Unknown,
}
