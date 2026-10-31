using TaskManager.Turns.Application.Clients.Tts;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Порт синтеза речи. Реализация — HTTP-клиент <c>tts</c>; Application не знает
/// про HTTP и формат контракта.
/// </summary>
public interface ITtsClient
{
    Task<TtsResult> SynthesizeAsync(
        TtsRequest request,
        CancellationToken cancellationToken = default);
}
