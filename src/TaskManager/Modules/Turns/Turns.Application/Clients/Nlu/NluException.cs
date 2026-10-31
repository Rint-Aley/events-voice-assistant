namespace TaskManager.Turns.Application.Clients.Nlu;

/// <summary>
/// Ошибка вызова <c>nlu</c>. Ответ 4xx — постоянная ошибка без повтора, 5xx, таймаут
/// и сетевая ошибка повторяются планировщиком.
/// </summary>
public sealed class NluException : Exception
{
    private readonly bool? _isPermanent;

    public NluException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        bool? isPermanent = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        _isPermanent = isPermanent;
    }

    /// <summary>HTTP-код ответа <c>nlu</c>, если он был.</summary>
    public int? StatusCode { get; }

    /// <summary>Повтор не поможет: запрос отвергнут или нарушен контракт.</summary>
    public bool IsPermanent => _isPermanent ?? StatusCode is >= 400 and < 500;
}
