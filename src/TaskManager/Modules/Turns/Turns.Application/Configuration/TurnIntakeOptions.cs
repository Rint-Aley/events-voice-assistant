namespace TaskManager.Turns.Application.Configuration;

/// <summary>
/// Настройки приёма запросов. Дедлайн хода по умолчанию совпадает с
/// <c>DEADLINE_SECONDS</c> из конфигурации MVP.
/// </summary>
public sealed record TurnIntakeOptions(TimeSpan Deadline)
{
    public static TurnIntakeOptions Default { get; } = new(TimeSpan.FromSeconds(90));
}
