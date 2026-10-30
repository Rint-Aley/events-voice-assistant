using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Queries;

/// <summary>
/// Актуальное состояние хода: то, что отдают <c>GET /turns/{id}</c> и снапшот SSE.
/// </summary>
public sealed record TurnSnapshot(
    Guid TurnId,
    TurnState State,
    string? Text,
    string? ErrorCode,
    TurnSnapshotAnswer? Answer);
