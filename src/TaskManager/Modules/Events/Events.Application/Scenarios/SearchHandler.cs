using TaskManager.Events.Application.Dtos;
using TaskManager.Events.Application.Ports;
using TaskManager.Events.Application.Queries;
using TaskManager.Events.Domain;
using TaskManager.SharedKernel;

namespace TaskManager.Events.Application.Scenarios;

/// <summary>
/// Семантический поиск по смыслу заголовка: top-3, только <c>active</c> и <c>done</c>,
/// <c>cancelled</c> исключён. Фильтры <c>type_filter</c> и период необязательны.
/// </summary>
/// <remarks>
/// Пустой результат — успех: <see cref="SearchResult.Events"/> пуст, а ход, который
/// вызывает сценарий, остаётся <c>COMPLETED</c>.
/// </remarks>
public sealed class SearchHandler
{
    /// <summary>Лимит поиска: три самые близкие записи.</summary>
    public const int Limit = 3;

    private readonly IEventQuery _query;

    public SearchHandler(IEventQuery query)
    {
        _query = query;
    }

    public async Task<Result<SearchResult>> HandleAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Неверный вектор — ожидаемая ошибка сценария, а не исключение: иначе PostgreSQL
        // вернёт 22000 different vector dimensions и ход получит 500 вместо NLU_FAILED.
        if (!IsEmbeddingValid(query.Embedding))
        {
            return Result<SearchResult>.Failure(EventErrors.InvalidEmbedding);
        }

        if (query.From is { } from && query.To is { } to && to < from)
        {
            return Result<SearchResult>.Failure(EventErrors.InvalidPeriod);
        }

        IReadOnlyList<Event> found = await _query.SearchAsync(
            query,
            Limit,
            cancellationToken);

        // Порт уже отсёк невидимые записи и применил type_filter; EventSelection.Filter
        // повторяет type_filter, чтобы Domain-правило не разошлось с SQL. Близость и
        // лимит задаёт только SQL: order by cosine distance + take.
        IReadOnlyList<EventDto> events = EventSelection
            .Filter(found, query.TypeFilter)
            .ToDtos();

        return Result<SearchResult>.Success(new SearchResult(events));
    }

    private static bool IsEmbeddingValid(IReadOnlyList<float>? embedding) =>
        embedding is { Count: EventEmbedding.Dimensions }
        && embedding.All(float.IsFinite);
}
