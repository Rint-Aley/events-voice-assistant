using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaskManager.Application.Abstractions;

namespace TaskManager.Infrastructure.Persistence;

/// <summary>
/// Транзакция живёт в сценарии Application. Репозитории только добавляют изменения.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork, IAsyncDisposable
{
    private readonly TaskManagerDbContext _dbContext;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(TaskManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active.");
        }

        _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No active transaction.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;

        // Откаченные Added/Modified-сущности не должны попасть в следующий SaveChanges
        // того же сценария: change tracker живёт дольше транзакции.
        _dbContext.ChangeTracker.Clear();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Откатывает незакоммиченную транзакцию, если сценарий упал между началом и
    /// коммитом, чтобы она не висела до dispose контекста.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync();
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
