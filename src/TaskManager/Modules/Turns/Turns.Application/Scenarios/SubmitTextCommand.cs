namespace TaskManager.Turns.Application.Scenarios;

/// <summary>Текстовый запрос пользователя. Ровно один из text/object_key задаёт API.</summary>
public sealed record SubmitTextCommand(
    Guid SessionId,
    Guid IdempotencyKey,
    string Text);
