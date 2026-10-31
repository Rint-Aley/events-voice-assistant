namespace TaskManager.Application.Abstractions;

/// <summary>
/// Транзакция и единица сохранения сценария. Открывается, коммитится и откатывается
/// только Application-сценарием; репозитории не коммитят. При уничтожении незакрытая
/// транзакция откатывается.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Открывает транзакцию сценария. Вложенные транзакции не поддерживаются.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет изменения без завершения транзакции: данные и состояние хода
    /// должны попадать в одну транзакцию.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
