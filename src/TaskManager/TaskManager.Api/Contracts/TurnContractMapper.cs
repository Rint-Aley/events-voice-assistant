using System.Globalization;
using TaskManager.Events.Domain;
using TaskManager.Turns.Application.Queries;
using TaskManager.Turns.Domain;

namespace TaskManager.Api.Contracts;

/// <summary>
/// Ручной маппинг Domain/Application → HTTP-контракт. Бизнес-решения не принимает:
/// только переводит состояния и записи в форму <c>docs/contracts/http-api.md</c>.
/// </summary>
public static class TurnContractMapper
{
    public static string ToContract(this TurnState state) =>
        state switch
        {
            TurnState.Transcribing => "TRANSCRIBING",
            TurnState.Interpreting => "INTERPRETING",
            TurnState.Applying => "APPLYING",
            TurnState.Completed => "COMPLETED",
            TurnState.NeedsClarification => "NEEDS_CLARIFICATION",
            TurnState.Failed => "FAILED",
            TurnState.Expired => "EXPIRED",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown turn state."),
        };

    public static EventResponse ToContract(this Event @event) =>
        new(
            @event.Id,
            @event.Kind switch
            {
                EventKind.Task => "task",
                EventKind.Reminder => "reminder",
                EventKind.Meeting => "meeting",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(@event),
                    @event.Kind,
                    "Unknown event kind."),
            },
            @event.Description,
            @event.StartsOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            @event.StartsAt?.ToString("HH:mm", CultureInfo.InvariantCulture),
            @event.IsDeadline);

    public static AnswerStreamData ToAnswerData(this TurnSnapshotAnswer answer) =>
        new(
            answer.Text,
            answer.Events.Select(ToContract).ToArray(),
            answer.TotalCount,
            answer.Warnings);

    public static TurnErrorResponse ToError(this string errorCode) =>
        new(errorCode, TurnErrorMessages.For(errorCode));
}

/// <summary>Тексты для <c>error</c> в снапшоте и SSE. Коды не переименовываются.</summary>
public static class TurnErrorMessages
{
    public static string For(string code) =>
        code switch
        {
            TurnErrors.EmptyTranscriptCode or TurnErrors.SttFailedCode =>
                "Не расслышала. Нажми на микрофон и повтори или напиши текстом.",
            TurnErrors.NluFailedCode =>
                "Не удалось разобрать запрос. Сформулируй его иначе.",
            TurnErrors.DomainApplyFailedCode =>
                "Не удалось сохранить запрос. Попробуй ещё раз.",
            TurnErrors.DeadlineExceededCode =>
                "Сервис сейчас недоступен. Попробуй через минуту.",
            _ => "Ход завершился ошибкой.",
        };
}
