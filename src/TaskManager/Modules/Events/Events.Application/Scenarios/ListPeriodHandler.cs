using TaskManager.Events.Application.Dtos;
using TaskManager.Events.Application.Ports;
using TaskManager.Events.Application.Queries;
using TaskManager.Events.Domain;
using TaskManager.SharedKernel;

namespace TaskManager.Events.Application.Scenarios;

/// <summary>
/// Возвращает список за период: до трёх записей и общее количество до применения лимита.
/// Порядок задаёт query-порт: <c>starts_on</c>, затем <c>starts_at</c>, записи без
/// времени — в конце дня, задачи без даты — после датированных.
/// </summary>
public sealed class ListPeriodHandler
{
    /// <summary>Лимит списка: три записи суммарно, включая блок «Без даты».</summary>
    public const int Limit = 3;

    private readonly IEventQuery _query;

    public ListPeriodHandler(IEventQuery query)
    {
        _query = query;
    }

    public async Task<Result<ListPeriodResult>> HandleAsync(
        ListPeriodQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.To < query.From)
        {
            return Result<ListPeriodResult>.Failure(EventErrors.InvalidPeriod);
        }

        EventListResult result = await _query.ListPeriodAsync(
            query.From,
            query.To,
            query.TypeFilter,
            Limit,
            cancellationToken);

        // Блоки собирает Domain: те же правила видимости и порядка, что и у поиска.
        EventListSections sections = EventSelection.ListSections(result.Events, query.TypeFilter);

        IReadOnlyList<EventDto> dated = sections.Dated.ToDtos();
        IReadOnlyList<EventDto> undated = sections.Undated.ToDtos();

        return Result<ListPeriodResult>.Success(new ListPeriodResult(
            dated.Concat(undated).ToList(),
            result.TotalCount,
            dated,
            undated));
    }
}
