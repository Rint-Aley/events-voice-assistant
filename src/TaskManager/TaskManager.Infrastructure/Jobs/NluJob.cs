using Microsoft.Extensions.Logging;
using Quartz;
using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Разбор текста. Постоянная ошибка <c>nlu</c> закрывает ход <c>NLU_FAILED</c> без
/// повтора, временная пробрасывается в Quartz.
/// </summary>
public sealed class NluJob : IJob
{
    private readonly InterpretTurnHandler _handler;
    private readonly FinalizeJobFailureHandler _finalizer;
    private readonly ILogger<NluJob> _logger;

    public NluJob(
        InterpretTurnHandler handler,
        FinalizeJobFailureHandler finalizer,
        ILogger<NluJob> logger)
    {
        _handler = handler;
        _finalizer = finalizer;
        _logger = logger;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        Guid turnId = JobData.TargetIdOf(context);

        try
        {
            Result<InterpretTurnResult> result = await _handler.HandleAsync(
                turnId,
                cancellationToken);

            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "nlu for turn {TurnId} finished with {ErrorCode}.",
                    turnId,
                    result.Error!.Code);
            }
        }
        catch (Exception error) when (JobFailureClassifier.IsPermanent(error))
        {
            await _finalizer.HandleAsync(JobKind.Nlu, turnId, cancellationToken);
        }
    }
}
