using Microsoft.EntityFrameworkCore;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Application.Queries;

namespace TaskManager.Infrastructure.Persistence.Queries;

public sealed class TurnQuery : ITurnQuery
{
    private readonly TaskManagerDbContext _dbContext;

    public TurnQuery(TaskManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TurnSnapshot?> GetSnapshotAsync(
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        var turn = await _dbContext.Turns
            .AsNoTracking()
            .Where(item => item.Id == turnId)
            .Select(item => new
            {
                item.State,
                item.Text,
                item.ErrorCode,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (turn is null)
        {
            return null;
        }

        var answer = await _dbContext.Answers
            .AsNoTracking()
            .Where(item => item.TurnId == turnId)
            .Select(item => new
            {
                item.Id,
                item.Text,
                item.TotalCount,
                item.Warnings,
                item.AudioObjectKey,
                item.AudioStatus,
                item.AudioDurationMs,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (answer is null)
        {
            return new TurnSnapshot(turnId, turn.State, turn.Text, turn.ErrorCode, Answer: null);
        }

        var events = await (
                from link in _dbContext.AnswerEvents.AsNoTracking()
                join @event in _dbContext.Events.AsNoTracking() on link.EventId equals @event.Id
                where link.AnswerId == answer.Id
                orderby link.Position
                select @event)
            .ToListAsync(cancellationToken);

        var snapshotAnswer = new TurnSnapshotAnswer(
            answer.Text,
            answer.TotalCount,
            answer.Warnings,
            answer.AudioObjectKey,
            answer.AudioStatus,
            answer.AudioDurationMs,
            events);

        return new TurnSnapshot(turnId, turn.State, turn.Text, turn.ErrorCode, snapshotAnswer);
    }
}
