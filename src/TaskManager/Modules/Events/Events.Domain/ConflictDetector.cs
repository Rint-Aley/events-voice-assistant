namespace TaskManager.Events.Domain;

/// <summary>
/// Ищет пересечения встреч. Конфликт — предупреждение: встреча сохраняется всегда.
/// </summary>
public static class ConflictDetector
{
    /// <summary>
    /// Возвращает все активные встречи, пересекающиеся с <paramref name="candidate"/>.
    /// Интервал считается полуоткрытым: <c>[start, start + duration)</c>.
    /// </summary>
    public static IReadOnlyList<Event> Detect(Event candidate, IEnumerable<Event> existing)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existing);

        if (candidate.Kind != EventKind.Meeting
            || candidate.Status != EventStatus.Active
            || candidate.Start is not { } candidateStart
            || candidate.End is not { } candidateEnd)
        {
            return [];
        }

        return existing
            .Where(existingEvent => existingEvent.Id != candidate.Id)
            .Where(existingEvent =>
                existingEvent.Kind == EventKind.Meeting
                && existingEvent.Status == EventStatus.Active)
            .Where(existingEvent =>
                existingEvent.Start is { } existingStart
                && existingEvent.End is { } existingEnd
                && existingStart < candidateEnd
                && candidateStart < existingEnd)
            .OrderBy(existingEvent => existingEvent.Start)
            .ToList();
    }
}
