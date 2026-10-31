namespace TaskManager.SharedKernel;

/// <summary>
/// Результат операции со значением: либо успех и значение, либо ожидаемая ошибка.
/// </summary>
/// <typeparam name="T">Тип значения успешной операции.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T _value;

    private Result(T value)
        : base(error: null)
    {
        _value = value;
    }

    private Result(DomainError error)
        : base(error)
    {
        _value = default!;
    }

    /// <summary>Значение успешной операции.</summary>
    /// <exception cref="InvalidOperationException">Операция завершилась ошибкой.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException(
            $"Result has no value: the operation failed with {Error!.Code}.");

    public static Result<T> Success(T value) => new(value);

    public new static Result<T> Failure(DomainError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result<T>(error);
    }
}
