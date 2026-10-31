using Microsoft.EntityFrameworkCore;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Infrastructure.Persistence.Repositories;

public sealed class AnswerRepository : IAnswerRepository
{
    private readonly TaskManagerDbContext _dbContext;

    public AnswerRepository(TaskManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Answer answer, CancellationToken cancellationToken = default)
    {
        await _dbContext.Answers.AddAsync(answer, cancellationToken);
    }

    public async Task AddEventAsync(AnswerEvent answerEvent, CancellationToken cancellationToken = default)
    {
        await _dbContext.AnswerEvents.AddAsync(answerEvent, cancellationToken);
    }

    public Task<Answer?> GetByIdAsync(Guid answerId, CancellationToken cancellationToken = default) =>
        _dbContext.Answers.FirstOrDefaultAsync(answer => answer.Id == answerId, cancellationToken);

    public async Task<Answer?> GetByIdForUpdateAsync(
        Guid answerId,
        CancellationToken cancellationToken = default)
    {
        // ToListAsync без композиции: EF не оборачивает FOR UPDATE в подзапрос.
        List<Answer> answers = await _dbContext.Answers
            .FromSqlInterpolated($"select * from answers where id = {answerId} for update")
            .ToListAsync(cancellationToken);

        return answers.FirstOrDefault();
    }

    public Task<Answer?> GetByTurnIdAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        _dbContext.Answers.FirstOrDefaultAsync(answer => answer.TurnId == turnId, cancellationToken);

    public async Task<IReadOnlyList<Answer>> ListPendingCreatedBeforeAsync(
        DateTimeOffset createdBefore,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Answers
            .AsNoTracking()
            .Where(answer => answer.AudioStatus == AnswerAudioStatus.Pending
                && answer.CreatedAt < createdBefore)
            .OrderBy(answer => answer.CreatedAt)
            .ThenBy(answer => answer.Id)
            .ToListAsync(cancellationToken);
    }
}
