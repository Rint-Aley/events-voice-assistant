namespace TaskManager.SharedKernel;

/// <summary>
/// Генератор идентификаторов сущностей. Позволяет подменять источник в тестах.
/// </summary>
public interface IIdGenerator
{
    Guid NewId();
}
