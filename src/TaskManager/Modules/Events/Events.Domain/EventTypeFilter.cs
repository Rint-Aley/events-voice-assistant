namespace TaskManager.Events.Domain;

/// <summary>
/// Необязательный фильтр выборки. Отсутствие фильтра означает «все типы».
/// </summary>
public enum EventTypeFilter
{
    Meeting = 1,
    Task = 2,
    Reminder = 3,
}
