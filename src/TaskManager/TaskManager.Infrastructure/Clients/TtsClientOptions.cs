namespace TaskManager.Infrastructure.Clients;

/// <summary>
/// Настройки HTTP-клиента <c>tts</c>. <see cref="Timeout"/> соответствует
/// <c>TTS_TIMEOUT</c> и по умолчанию равен 20 секундам.
/// </summary>
public sealed record TtsClientOptions(TimeSpan Timeout)
{
    public static TtsClientOptions Default { get; } = new(TimeSpan.FromSeconds(20));
}
