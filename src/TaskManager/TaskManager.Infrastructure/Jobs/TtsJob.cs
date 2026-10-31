using Microsoft.Extensions.Logging;
using Quartz;
using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Scenarios;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Синтез озвучки. Провал не меняет состояние хода: при постоянной ошибке и после
/// исчерпания попыток <c>audio_status</c> становится <c>failed</c>.
/// </summary>
public sealed class TtsJob : IJob
{
    private readonly SynthesizeAnswerHandler _handler;
    private readonly FinalizeJobFailureHandler _finalizer;
    private readonly ILogger<TtsJob> _logger;

    public TtsJob(
        SynthesizeAnswerHandler handler,
        FinalizeJobFailureHandler finalizer,
        ILogger<TtsJob> logger)
    {
        _handler = handler;
        _finalizer = finalizer;
        _logger = logger;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        Guid answerId = JobData.TargetIdOf(context);

        try
        {
            Result<SynthesizeAnswerResult> result = await _handler.HandleAsync(
                answerId,
                cancellationToken);

            if (result.IsFailure)
            {
                // Ошибка синтеза не меняет ход: текст уже отдан, аудио остаётся без озвучки.
                _logger.LogWarning(
                    "tts for answer {AnswerId} finished with {ErrorCode}.",
                    answerId,
                    result.Error!.Code);
            }
        }
        catch (Exception error) when (JobFailureClassifier.IsPermanent(error))
        {
            await _finalizer.HandleAsync(JobKind.Tts, answerId, cancellationToken);
        }
    }
}
