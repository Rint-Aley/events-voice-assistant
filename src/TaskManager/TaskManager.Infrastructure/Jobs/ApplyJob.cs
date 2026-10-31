using Microsoft.Extensions.Logging;
using Quartz;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Тик <c>apply</c>: выбирает ходы с готовыми к применению командами и применяет каждый
/// отдельной транзакцией. Ожидаемые ошибки домена обрабатывает сценарий применения
/// (backoff, три попытки, <c>DOMAIN_APPLY_FAILED</c>), поэтому тик не держит свой цикл.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ApplyJob : IJob
{
    private const int BatchSize = 50;

    private readonly IDomainCommandRepository _commands;
    private readonly ApplyTurnPlanHandler _apply;
    private readonly IClock _clock;
    private readonly ILogger<ApplyJob> _logger;

    public ApplyJob(
        IDomainCommandRepository commands,
        ApplyTurnPlanHandler apply,
        IClock clock,
        ILogger<ApplyJob> logger)
    {
        _commands = commands;
        _apply = apply;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> turnIds = await _commands.ListDueTurnIdsAsync(
            _clock.UtcNow,
            BatchSize,
            cancellationToken);

        foreach (Guid turnId in turnIds)
        {
            try
            {
                await _apply.HandleAsync(turnId, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Следующий тик повторит выборку; один зависший ход не должен
                // останавливать применение остальных.
                _logger.LogError(error, "Apply tick failed for turn {TurnId}.", turnId);
            }
        }
    }
}
