namespace TaskManager.Turns.Domain;

/// <summary>Статус применения команды домена.</summary>
public enum DomainCommandStatus
{
    /// <summary>Команда ждёт применения.</summary>
    Pending,

    /// <summary>Команда применена.</summary>
    Applied,

    /// <summary>Попытки исчерпаны.</summary>
    Failed,
}
