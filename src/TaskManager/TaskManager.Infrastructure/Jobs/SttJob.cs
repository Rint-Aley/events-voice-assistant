using Microsoft.Extensions.Logging;
using Quartz;
using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Распознавание речи. <c>4xx</c> и нарушение контракта — постоянная ошибка: ход сразу
/// закрывается <c>STT_FAILED</c>. <c>5xx</c>, таймаут и сеть пробрасываются, чтобы Quartz
/// повторил задачу по retry policy; после исчерпания срабатывает <see cref="RetryExhaustedListener"/>.
/// </summary>
public sealed class SttJob : IJob
{
    private readonly TranscribeTurnHandler _handler;
    private readonly FinalizeJobFailureHandler _finalizer;
    private readonly ILogger<SttJob> _logger;

    public SttJob(
        TranscribeTurnHandler handler,
        FinalizeJobFailureHandler finalizer,
        ILogger<SttJob> logger)
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
            Result<SubmitTurnResult> result = await _handler.HandleAsync(
                turnId,
                cancellationToken);

            if (result.IsFailure)
            {
                // Ожидаемая ошибка не бросается: ход не должен зависнуть в INTERPRETING
                // без следа, поэтому фиксируем код и оставляем закрытие reconciler'у.
                _logger.LogWarning(
                    "stt for turn {TurnId} finished with {ErrorCode}.",
                    turnId,
                    result.Error!.Code);
            }
        }
        catch (Exception error) when (JobFailureClassifier.IsPermanent(error))
        {
            await _finalizer.HandleAsync(JobKind.Stt, turnId, cancellationToken);
        }
    }
}
