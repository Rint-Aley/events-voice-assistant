namespace TaskManager.Turns.Application.Clients.Nlu;

/// <summary>
/// Ответ <c>nlu</c>: действие, сущности, недостающие слоты, вопрос и эмбеддинг.
/// Новые значения <c>action</c> не добавляются.
/// </summary>
public sealed record NluResponse(
    Guid TurnId,
    string? Action,
    double Confidence,
    NluEntities? Entities = null,
    IReadOnlyList<string>? Missing = null,
    string? Question = null,
    NluEmbedding? Embedding = null);
