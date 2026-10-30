namespace TaskManager.Events.Domain;

/// <summary>Тип записи пользователя.</summary>
public enum EventKind
{
    Task = 1,
    Reminder = 2,
    Meeting = 3,
}
