namespace TaskManager.Turns.Domain;

/// <summary>Тип записи, которую не удалось собрать в черновик.</summary>
public enum DraftKind
{
    /// <summary>Задача.</summary>
    Task,

    /// <summary>Напоминание.</summary>
    Reminder,

    /// <summary>Встреча.</summary>
    Meeting,
}
