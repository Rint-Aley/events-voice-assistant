namespace TaskManager.SharedKernel;

/// <summary>
/// Ожидаемая ошибка домена или сценария: код для контракта, сообщение и признак
/// того, что ошибка штатная (а не инфраструктурный сбой).
/// </summary>
public sealed record DomainError
{
    private DomainError(string code, string message, bool isExpected)
    {
        Code = code;
        Message = message;
        IsExpected = isExpected;
    }

    /// <summary>Стабильный код ошибки. Коды не переименовываются.</summary>
    public string Code { get; }

    /// <summary>Текст ошибки для ответа пользователю или для лога.</summary>
    public string Message { get; }

    /// <summary>
    /// <see langword="true"/> для штатных нарушений правил (валидация, состояние, конфликт);
    /// <see langword="false"/> для неожидаемых сбоев, которые не являются частью бизнес-логики.
    /// </summary>
    public bool IsExpected { get; }

    public static DomainError Expected(string code, string message) =>
        Create(code, message, isExpected: true);

    public static DomainError Unexpected(string code, string message) =>
        Create(code, message, isExpected: false);

    private static DomainError Create(string code, string message, bool isExpected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);

        return new DomainError(code, message, isExpected);
    }
}
