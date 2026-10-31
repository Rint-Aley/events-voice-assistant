namespace TaskManager.Turns.Application.Configuration;

/// <summary>
/// Настройки синтеза речи. Значения по умолчанию заменяются конфигурацией
/// <c>TTS_VOICE</c>, <c>TTS_FORMAT</c> и <c>TTS_SPEED</c>.
/// </summary>
public sealed record TtsOptions(string Voice, string Format, double Speed)
{
    public static TtsOptions Default { get; } = new("default", "ogg_opus", 1);
}
