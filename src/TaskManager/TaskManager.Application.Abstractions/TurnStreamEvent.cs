namespace TaskManager.Application.Abstractions;

/// <summary>
/// Уведомление о ходе для живого потока SSE. Порт не знает про JSON и формат фреймов:
/// Api сам собирает <c>data</c> из снапшота хода.
/// </summary>
/// <param name="TurnId">Ход, к которому относится событие.</param>
/// <param name="Kind">Вид события.</param>
public sealed record TurnStreamEvent(Guid TurnId, TurnStreamEventKind Kind)
{
    /// <summary>Состояние хода; заполнено только для <see cref="TurnStreamEventKind.State"/>.</summary>
    public string? State { get; init; }

    /// <summary>Код ошибки; заполнен только для <see cref="TurnStreamEventKind.Error"/>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Текст ошибки; заполнен только для <see cref="TurnStreamEventKind.Error"/>.</summary>
    public string? ErrorMessage { get; init; }

    public static TurnStreamEvent StateChanged(Guid turnId, string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        return new TurnStreamEvent(turnId, TurnStreamEventKind.State) { State = state };
    }

    public static TurnStreamEvent AnswerReady(Guid turnId) =>
        new(turnId, TurnStreamEventKind.Answer);

    public static TurnStreamEvent AudioReady(Guid turnId) =>
        new(turnId, TurnStreamEventKind.Audio);

    public static TurnStreamEvent Failed(Guid turnId, string errorCode, string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentNullException.ThrowIfNull(errorMessage);

        return new TurnStreamEvent(turnId, TurnStreamEventKind.Error)
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
        };
    }
}
