using TaskManager.Turns.Application.Clients.Nlu;

namespace TaskManager.Turns.Application.Ports;

/// <summary>
/// Порт разбора текста. <c>nlu</c> отдаёт план, а не решение о записи; нормализацию
/// и переходы выполняет сценарий Turns.
/// </summary>
public interface INluClient
{
    Task<NluResponse> InterpretAsync(
        NluRequest request,
        CancellationToken cancellationToken = default);
}
