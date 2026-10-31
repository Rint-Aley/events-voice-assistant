using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Итог закрытия хода. <see cref="Changed"/> отделяет реальный переход от
/// идемпотентного повтора: <c>reconciler</c> считает только изменённые ходы.
/// </summary>
public readonly record struct TurnCloseResult(TurnState State, bool Changed);

/// <summary>
/// Итог отметки озвучки. <see cref="Changed"/> отделяет реальный переход от повтора
/// после готового или уже проваленного аудио.
/// </summary>
public readonly record struct AnswerCloseResult(AnswerAudioStatus Status, bool Changed);
