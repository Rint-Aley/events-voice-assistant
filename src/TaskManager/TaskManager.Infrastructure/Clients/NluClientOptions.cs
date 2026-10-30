namespace TaskManager.Infrastructure.Clients;

/// <summary>
/// Настройки HTTP-клиента <c>nlu</c>. <see cref="Timeout"/> соответствует
/// <c>NLU_TIMEOUT</c> и по умолчанию равен 20 секундам.
/// </summary>
public sealed record NluClientOptions(TimeSpan Timeout)
{
    public static NluClientOptions Default { get; } = new(TimeSpan.FromSeconds(20));
}
