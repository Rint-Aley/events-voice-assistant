namespace TaskManager.Events.Domain;

/// <summary>
/// Статус записи. В MVP создание всегда даёт <see cref="Active"/>; переходы — LATER.
/// </summary>
public enum EventStatus
{
    Active = 1,
    Done = 2,
    Cancelled = 3,
}
