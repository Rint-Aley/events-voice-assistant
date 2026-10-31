using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Финальная обработка исчерпанных попыток задачи. <c>stt</c>/<c>nlu</c> закрывают ход
/// ошибкой с кодом, <c>tts</c> помечает только озвучку. <c>apply</c> закрывает ход
/// <c>DOMAIN_APPLY_FAILED</c> сам после трёх попыток команды. Терминальные ходы и
/// готовое аудио не меняются.
/// </summary>
public sealed class FinalizeJobFailureHandler
{
    private readonly FailTurnHandler _failTurn;
    private readonly MarkAnswerAudioFailedHandler _failAudio;

    public FinalizeJobFailureHandler(
        FailTurnHandler failTurn,
        MarkAnswerAudioFailedHandler failAudio)
    {
        _failTurn = failTurn;
        _failAudio = failAudio;
    }

    public async Task HandleAsync(
        JobKind kind,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        switch (kind)
        {
            case JobKind.Stt:
                await _failTurn.HandleAsync(
                    targetId,
                    TurnTransition.SttFailed,
                    cancellationToken);
                break;

            case JobKind.Nlu:
                await _failTurn.HandleAsync(
                    targetId,
                    TurnTransition.NluFailed,
                    cancellationToken);
                break;

            case JobKind.Tts:
                await _failAudio.HandleAsync(targetId, cancellationToken);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
