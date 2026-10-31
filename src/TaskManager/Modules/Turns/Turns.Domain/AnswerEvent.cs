using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>Связь ответа с событиями списка или поиска; позиция задаёт порядок.</summary>
public sealed class AnswerEvent
{
    private AnswerEvent()
    {
    }

    private AnswerEvent(Guid answerId, Guid eventId, short position)
    {
        AnswerId = answerId;
        EventId = eventId;
        Position = position;
    }

    /// <summary>Ответ, который ссылается на событие.</summary>
    public Guid AnswerId { get; }

    /// <summary>Событие из ответа.</summary>
    public Guid EventId { get; }

    /// <summary>Порядок события в ответе.</summary>
    public short Position { get; }

    /// <summary>Создаёт связь ответа с событием.</summary>
    public static Result<AnswerEvent> Create(Guid answerId, Guid eventId, short position = 0)
    {
        if (answerId == Guid.Empty)
        {
            return Result<AnswerEvent>.Failure(AnswerEventErrors.AnswerIdRequired);
        }

        if (eventId == Guid.Empty)
        {
            return Result<AnswerEvent>.Failure(AnswerEventErrors.EventIdRequired);
        }

        if (position < 0)
        {
            return Result<AnswerEvent>.Failure(AnswerEventErrors.PositionInvalid);
        }

        return Result<AnswerEvent>.Success(new AnswerEvent(answerId, eventId, position));
    }
}
