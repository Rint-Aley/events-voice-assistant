using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>Итог синтеза ответа.</summary>
public sealed record SynthesizeAnswerResult(
    Guid AnswerId,
    AnswerAudioStatus AudioStatus);
