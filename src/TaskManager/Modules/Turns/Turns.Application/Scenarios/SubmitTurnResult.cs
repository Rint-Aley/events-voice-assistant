using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Результат приёма запроса: созданный или найденный по ключу идемпотентности ход.
/// </summary>
public sealed record SubmitTurnResult(Guid TurnId, TurnState State);
