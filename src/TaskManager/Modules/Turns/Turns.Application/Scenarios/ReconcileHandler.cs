using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Сверка состояния: закрывает просроченные ходы, добивает отсутствующие задачи,
/// помечает зависшую озвучку и страхует <see cref="TurnState.Applying"/> с проваленными
/// командами. Постановка задачи идемпотентна, поэтому повторный проход безопасен.
/// </summary>
public sealed class ReconcileHandler
{
    /// <summary>Ответ без задачи озвучки дольше этого срока считается зависшим.</summary>
    public static readonly TimeSpan StaleAudioAge = TimeSpan.FromSeconds(60);

    private readonly ITurnRepository _turns;
    private readonly IAnswerRepository _answers;
    private readonly IDomainCommandRepository _commands;
    private readonly IJobScheduler _scheduler;
    private readonly FailTurnHandler _failTurn;
    private readonly MarkAnswerAudioFailedHandler _failAudio;
    private readonly IClock _clock;

    public ReconcileHandler(
        ITurnRepository turns,
        IAnswerRepository answers,
        IDomainCommandRepository commands,
        IJobScheduler scheduler,
        FailTurnHandler failTurn,
        MarkAnswerAudioFailedHandler failAudio,
        IClock clock)
    {
        _turns = turns;
        _answers = answers;
        _commands = commands;
        _scheduler = scheduler;
        _failTurn = failTurn;
        _failAudio = failAudio;
        _clock = clock;
    }

    public async Task<ReconcileResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _clock.UtcNow;
        IReadOnlyList<Turn> active = await _turns.ListActiveAsync(cancellationToken);

        int expired = 0;
        int scheduled = 0;
        int applyFailed = 0;

        foreach (Turn turn in active)
        {
            if (turn.DeadlineAt <= now
                && turn.State is TurnState.Transcribing or TurnState.Interpreting)
            {
                Result<TurnCloseResult> closed = await _failTurn.HandleAsync(
                    turn.Id,
                    TurnTransition.DeadlineExceeded,
                    cancellationToken);

                if (closed.IsSuccess && closed.Value.Changed)
                {
                    expired++;
                }

                continue;
            }

            if (turn.State == TurnState.Transcribing
                && !await _scheduler.ExistsAsync(JobKind.Stt, turn.Id, cancellationToken))
            {
                await _scheduler.ScheduleAsync(JobKind.Stt, turn.Id, cancellationToken);
                scheduled++;
            }

            if (turn.State == TurnState.Interpreting
                && !await _scheduler.ExistsAsync(JobKind.Nlu, turn.Id, cancellationToken))
            {
                await _scheduler.ScheduleAsync(JobKind.Nlu, turn.Id, cancellationToken);
                scheduled++;
            }

            if (turn.State == TurnState.Applying)
            {
                IReadOnlyList<DomainCommand> all = await _commands.ListByTurnAsync(
                    turn.Id,
                    cancellationToken);

                if (all.Count > 0
                    && all.All(command => command.Status == DomainCommandStatus.Failed))
                {
                    Result<TurnCloseResult> closed = await _failTurn.HandleAsync(
                        turn.Id,
                        TurnTransition.ApplyFailed,
                        cancellationToken);

                    if (closed.IsSuccess && closed.Value.Changed)
                    {
                        applyFailed++;
                    }
                }
            }
        }

        IReadOnlyList<Answer> stale = await _answers.ListPendingCreatedBeforeAsync(
            now - StaleAudioAge,
            cancellationToken);

        int audioFailed = 0;

        foreach (Answer answer in stale)
        {
            if (await _scheduler.ExistsAsync(JobKind.Tts, answer.Id, cancellationToken))
            {
                continue;
            }

            Result<AnswerCloseResult> failed = await _failAudio.HandleAsync(
                answer.Id,
                cancellationToken);

            if (failed.IsSuccess && failed.Value.Changed)
            {
                audioFailed++;
            }
        }

        return new ReconcileResult(expired, scheduled, audioFailed, applyFailed);
    }
}
