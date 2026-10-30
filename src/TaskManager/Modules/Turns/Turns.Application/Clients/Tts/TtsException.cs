namespace TaskManager.Turns.Application.Clients.Tts;

/// <summary>
/// Ошибка вызова <c>tts</c>. По <c>implementation.md</c> §6.4 ответ 4xx — постоянная
/// ошибка без повтора, 5xx, таймаут и сетевая ошибка повторяются планировщиком.
/// </summary>
public sealed class TtsException : Exception
{
    private readonly bool? _isPermanent;

    public TtsException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        bool? isPermanent = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        _isPermanent = isPermanent;
    }

    /// <summary>HTTP-код ответа <c>tts</c>, если он был.</summary>
    public int? StatusCode { get; }

    /// <summary>Повтор не поможет: контракт нарушен или запрос отвергнут.</summary>
    public bool IsPermanent => _isPermanent ?? StatusCode is >= 400 and < 500;
}
