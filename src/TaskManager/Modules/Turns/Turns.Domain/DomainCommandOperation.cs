namespace TaskManager.Turns.Domain;

/// <summary>Операция команды домена.</summary>
public enum DomainCommandOperation
{
    /// <summary>Создать событие; в хранилище — <c>event.create</c>.</summary>
    EventCreate,

    /// <summary>Обновить событие; в хранилище — <c>event.update</c>; в MVP зарезервировано.</summary>
    EventUpdate,
}
