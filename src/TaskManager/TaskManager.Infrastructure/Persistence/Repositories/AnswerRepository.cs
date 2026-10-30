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

    public Task<Answer?> GetByTurnIdAsync(Guid turnId, CancellationToken cancellationToken = default) =>
        _dbContext.Answers.FirstOrDefaultAsync(answer => answer.TurnId == turnId, cancellationToken);
}
