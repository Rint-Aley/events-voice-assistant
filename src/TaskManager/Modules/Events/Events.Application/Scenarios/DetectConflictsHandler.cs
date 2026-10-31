using TaskManager.Events.Application.Dtos;
using TaskManager.Events.Application.Ports;
using TaskManager.Events.Domain;

namespace TaskManager.Events.Application.Scenarios;

/// <summary>
/// Ищет активные встречи, пересекающиеся с кандидатом. Возвращает все пересечения:
/// они попадают в <c>warnings</c> ответа, а встреча сохраняется всегда.
/// </summary>
public sealed class DetectConflictsHandler
{
    private readonly IEventQuery _query;

    public DetectConflictsHandler(IEventQuery query)
    {
        _query = query;
    }

    public async Task<IReadOnlyList<EventDto>> HandleAsync(
        Event candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Kind != EventKind.Meeting
            || candidate.Status != EventStatus.Active
            || candidate.Start is not { } start
            || candidate.End is not { } end)
        {
            return Array.Empty<EventDto>();
        }

        IReadOnlyList<Event> existing = await _query.ListActiveMeetingCandidatesAsync(
            start,
            end,
            cancellationToken);

        return ConflictDetector.Detect(candidate, existing).ToDtos();
    }
}
