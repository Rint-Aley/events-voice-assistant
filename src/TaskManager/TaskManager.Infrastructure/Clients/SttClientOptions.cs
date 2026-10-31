namespace TaskManager.Infrastructure.Clients;

/// <summary>
/// Настройки HTTP-клиента <c>stt</c>. <see cref="Timeout"/> соответствует
/// <c>STT_TIMEOUT</c> и по умолчанию равен 30 секундам.
/// </summary>
public sealed record SttClientOptions(TimeSpan Timeout)
{
    public static SttClientOptions Default { get; } = new(TimeSpan.FromSeconds(30));
}
