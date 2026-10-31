using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Queries;

/// <summary>
/// Семантический поиск по <c>description</c>. Вектор приходит готовым от <c>nlu</c>;
/// модель — <c>text-embedding-3-small</c>, размерность — 1536.
/// </summary>
/// <remarks>
/// Границы периода необязательны и применяются по <c>starts_on</c> включительно.
/// Отсутствие фильтра типа означает «все типы».
/// </remarks>
public sealed record SearchQuery(
    IReadOnlyList<float> Embedding,
    EventTypeFilter? TypeFilter = null,
    DateOnly? From = null,
    DateOnly? To = null);
