using TaskManager.Application.Abstractions;
using TaskManager.Events.Application.Commands;
using TaskManager.Events.Application.Dtos;
using TaskManager.Events.Application.Ports;
using TaskManager.Events.Domain;
using TaskManager.SharedKernel;

namespace TaskManager.Events.Application.Scenarios;

/// <summary>
/// Создаёт запись: валидирует слоты через Domain, собирает предупреждения о
/// пересечениях встреч и сохраняет запись и конфликты.
/// </summary>
/// <remarks>
/// Сценарий не открывает и не коммитит транзакцию: сценарий применения команд хода
/// обязан открыть её через <see cref="IUnitOfWork.BeginTransactionAsync"/> до вызова и
/// закоммитить после успеха. <see cref="IUnitOfWork.SaveChangesAsync"/> только фиксирует
/// изменения внутри этой транзакции; без неё событие закоммитится отдельно от
/// <c>answer</c>, <c>answer_events</c> и <c>turns.state</c>.
/// </remarks>
public sealed class CreateEventHandler
{
    private readonly IEventRepository _repository;
    private readonly DetectConflictsHandler _detectConflicts;
    private readonly IUnitOfWork _unitOfWork;

    public CreateEventHandler(
        IEventRepository repository,
        DetectConflictsHandler detectConflicts,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _detectConflicts = detectConflicts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CreateEventResult>> HandleAsync(
        CreateEventCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var created = Event.Create(
            command.Kind,
            command.Description,
            command.SourceText,
            command.StartsOn,
            command.StartsAt,
            command.Duration,
            command.IsDeadline);

        if (created.IsFailure)
        {
            return Result<CreateEventResult>.Failure(created.Error!);
        }

        Event @event = created.Value;

        IReadOnlyList<EventDto> conflicts = await _detectConflicts.HandleAsync(
            @event,
            cancellationToken);

        await _repository.AddAsync(@event, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<CreateEventResult>.Success(
            new CreateEventResult(@event.ToDto(), conflicts));
    }
}
