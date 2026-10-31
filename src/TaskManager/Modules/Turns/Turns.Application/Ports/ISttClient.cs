using TaskManager.Turns.Application.Clients.Stt;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Порт распознавания речи. Реализация — HTTP-клиент <c>stt</c>; Application не знает
/// про HTTP и формат контракта.
/// </summary>
public interface ISttClient
{
    Task<SttResult> TranscribeAsync(
        SttRequest request,
        CancellationToken cancellationToken = default);
}
