using TaskManager.Events.Domain;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Queries;

/// <summary>
/// Готовая часть ответа: текст, список записей, счётчик и аудио.
/// </summary>
public sealed record TurnSnapshotAnswer(
    string Text,
    int? TotalCount,
    IReadOnlyList<string> Warnings,
    string? AudioObjectKey,
    AnswerAudioStatus AudioStatus,
    int? AudioDurationMs,
    IReadOnlyList<Event> Events);
