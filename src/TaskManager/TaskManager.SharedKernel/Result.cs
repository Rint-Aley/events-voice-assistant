namespace TaskManager.SharedKernel;

/// <summary>
/// Результат операции без значения: либо успех, либо ожидаемая ошибка.
/// </summary>
public class Result
{
    private protected Result(DomainError? error)
    {
        Error = error;
    }

    /// <summary>Ошибка операции или <see langword="null"/>, если операция успешна.</summary>
    public DomainError? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(error: null);

    public static Result Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result(error);
    }
}
