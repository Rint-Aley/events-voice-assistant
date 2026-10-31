using Microsoft.EntityFrameworkCore;
using TaskManager.Events.Application.Ports;
using TaskManager.Events.Domain;

namespace TaskManager.Infrastructure.Persistence.Repositories;

public sealed class EventRepository : IEventRepository
{
    private readonly TaskManagerDbContext _dbContext;

    public EventRepository(TaskManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Event @event, CancellationToken cancellationToken = default)
    {
        await _dbContext.Events.AddAsync(@event, cancellationToken);
    }

    public Task<Event?> GetByIdAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        _dbContext.Events.FirstOrDefaultAsync(@event => @event.Id == eventId, cancellationToken);
}
