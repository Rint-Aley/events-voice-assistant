using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Итог разбора <c>nlu</c>. <see cref="Intent"/> равен <see langword="null"/>, если план
/// не удалось разобрать и ход закрыт ошибкой <c>NLU_FAILED</c>.
/// </summary>
public sealed record InterpretTurnResult(
    Guid TurnId,
    TurnState State,
    TurnIntent? Intent);
