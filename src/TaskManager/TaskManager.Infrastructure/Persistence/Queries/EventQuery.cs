using Microsoft.EntityFrameworkCore;
using TaskManager.Events.Application.Ports;
using TaskManager.Events.Application.Queries;
using TaskManager.Events.Domain;

namespace TaskManager.Infrastructure.Persistence.Queries;

public sealed class EventQuery : IEventQuery
{
    private readonly TaskManagerDbContext _dbContext;

    public EventQuery(TaskManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EventListResult> ListPeriodAsync(
        DateOnly from,
        DateOnly to,
        EventTypeFilter? typeFilter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return new EventListResult(Array.Empty<Event>(), 0);
        }

        var query = VisibleEvents()
            .Where(@event => !@event.StartsOn.HasValue
                || (@event.StartsOn >= from && @event.StartsOn <= to));

        query = ApplyTypeFilter(query, typeFilter);

        // Счётчик и страница читаются одним SQL-запросом: иначе параллельная запись между
        // ними разведёт total_count и выдачу. Порядок совпадает с EventSelection.SortForList,
        // Id — детерминированный tiebreaker для записей с одинаковыми датой и временем.
        var rows = await query
            .OrderBy(@event => @event.StartsOn.HasValue ? 0 : 1)
            .ThenBy(@event => @event.StartsOn)
            .ThenBy(@event => @event.StartsAt.HasValue ? 0 : 1)
            .ThenBy(@event => @event.StartsAt)
            .ThenBy(@event => @event.Id)
            .Take(limit)
            .Select(@event => new { Event = @event, TotalCount = query.Count() })
            .ToListAsync(cancellationToken);

        // Пустая страница возможна только при пустой выборке, тогда и total_count равен нулю.
        var totalCount = rows.Count == 0 ? 0 : rows[0].TotalCount;

        return new EventListResult(rows.Select(row => row.Event).ToList(), totalCount);
    }

    public async Task<IReadOnlyList<Event>> ListActiveMeetingCandidatesAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            return Array.Empty<Event>();
        }

        // NLU ограничивает duration_minutes значением 1440 (nlu.response.json), поэтому
        // встреча длиннее суток не создаётся. Значит, пересечь начало окна может только
        // встреча, начавшаяся не раньше предыдущего дня; этого диапазона достаточно и он
        // остаётся range-scan по events_by_status (status, starts_on).
        // Точное пересечение проверяет ConflictDetector: кандидаты — надмножество.
        var earliestStartDate = DateOnly.FromDateTime(from).AddDays(-1);
        var latestStartDate = DateOnly.FromDateTime(to);

        return await _dbContext.Events
            .AsNoTracking()
            .Where(@event => @event.Kind == EventKind.Meeting)
            .Where(@event => @event.Status == EventStatus.Active)
            .Where(@event => @event.StartsOn >= earliestStartDate)
            .Where(@event => @event.StartsOn <= latestStartDate)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Event> VisibleEvents() =>
        _dbContext.Events
            .AsNoTracking()
            .Where(@event => @event.Status == EventStatus.Active || @event.Status == EventStatus.Done);

    private static IQueryable<Event> ApplyTypeFilter(
        IQueryable<Event> query,
        EventTypeFilter? typeFilter) =>
        typeFilter switch
        {
            EventTypeFilter.Meeting => query.Where(@event => @event.Kind == EventKind.Meeting),
            EventTypeFilter.Task => query.Where(@event => @event.Kind == EventKind.Task || @event.Kind == EventKind.Reminder),
            EventTypeFilter.Reminder => query.Where(@event => @event.Kind == EventKind.Reminder),
            _ => query,
        };
}
