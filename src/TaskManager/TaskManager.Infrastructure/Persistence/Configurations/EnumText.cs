using TaskManager.Events.Domain;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Configurations;

/// <summary>
/// Текстовые значения enum в БД. Имена колонок <c>text</c>, поэтому конвертация явная.
/// </summary>
internal static class EnumText
{
    public static string ToDb(TurnState state) => state switch
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

    public static TurnState TurnStateFromDb(string value) => value switch
    {
        "TRANSCRIBING" => TurnState.Transcribing,
        "INTERPRETING" => TurnState.Interpreting,
        "APPLYING" => TurnState.Applying,
        "COMPLETED" => TurnState.Completed,
        "NEEDS_CLARIFICATION" => TurnState.NeedsClarification,
        "FAILED" => TurnState.Failed,
        "EXPIRED" => TurnState.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown turn state."),
    };

    public static string ToDb(DraftKind kind) => kind switch
    {
        DraftKind.Task => "task",
        DraftKind.Reminder => "reminder",
        DraftKind.Meeting => "meeting",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown draft kind."),
    };

    public static DraftKind DraftKindFromDb(string value) => value switch
    {
        "task" => DraftKind.Task,
        "reminder" => DraftKind.Reminder,
        "meeting" => DraftKind.Meeting,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown draft kind."),
    };

    public static string ToDb(EventKind kind) => kind switch
    {
        EventKind.Task => "task",
        EventKind.Reminder => "reminder",
        EventKind.Meeting => "meeting",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown event kind."),
    };

    public static EventKind EventKindFromDb(string value) => value switch
    {
        "task" => EventKind.Task,
        "reminder" => EventKind.Reminder,
        "meeting" => EventKind.Meeting,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown event kind."),
    };

    public static string ToDb(EventStatus status) => status switch
    {
        EventStatus.Active => "active",
        EventStatus.Done => "done",
        EventStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown event status."),
    };

    public static EventStatus EventStatusFromDb(string value) => value switch
    {
        "active" => EventStatus.Active,
        "done" => EventStatus.Done,
        "cancelled" => EventStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown event status."),
    };

    public static string ToDb(AnswerAudioStatus status) => status switch
    {
        AnswerAudioStatus.Pending => "pending",
        AnswerAudioStatus.Ready => "ready",
        AnswerAudioStatus.Failed => "failed",
        AnswerAudioStatus.Skipped => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown audio status."),
    };

    public static AnswerAudioStatus AudioStatusFromDb(string value) => value switch
    {
        "pending" => AnswerAudioStatus.Pending,
        "ready" => AnswerAudioStatus.Ready,
        "failed" => AnswerAudioStatus.Failed,
        "skipped" => AnswerAudioStatus.Skipped,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown audio status."),
    };

    public static string ToDb(DomainCommandOperation operation) => operation switch
    {
        DomainCommandOperation.EventCreate => "event.create",
        DomainCommandOperation.EventUpdate => "event.update",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown command operation."),
    };

    public static DomainCommandOperation OperationFromDb(string value) => value switch
    {
        "event.create" => DomainCommandOperation.EventCreate,
        "event.update" => DomainCommandOperation.EventUpdate,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown command operation."),
    };

    public static string ToDb(DomainCommandStatus status) => status switch
    {
        DomainCommandStatus.Pending => "pending",
        DomainCommandStatus.Applied => "applied",
        DomainCommandStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown command status."),
    };

    public static DomainCommandStatus CommandStatusFromDb(string value) => value switch
    {
        "pending" => DomainCommandStatus.Pending,
        "applied" => DomainCommandStatus.Applied,
        "failed" => DomainCommandStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown command status."),
    };
}
