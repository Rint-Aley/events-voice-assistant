using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Clients.Tts;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Постоянные ошибки внешних сервисов: <c>4xx</c>, невалидный контракт, ошибка валидации.
/// Такие ошибки не повторяются; всё остальное (<c>5xx</c>, таймаут, сеть) уходит в Quartz.
/// </summary>
internal static class JobFailureClassifier
{
    public static bool IsPermanent(Exception exception) =>
        exception switch
        {
            SttException stt => stt.IsPermanent,
            NluException nlu => nlu.IsPermanent,
            TtsException tts => tts.IsPermanent,
            _ => false,
        };
}
