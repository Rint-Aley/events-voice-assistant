using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>Итог применения команд хода.</summary>
public sealed record ApplyTurnPlanResult(
    Guid TurnId,
    TurnState State,
    Guid? AnswerId,
    string? Text);
