namespace TaskManager.Turns.Application.Scenarios;

/// <summary>Подтверждение отмены черновика; текст одинаков для первого и повторного вызова.</summary>
public sealed record CancelDraftResult(string Text);
