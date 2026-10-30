using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>
/// Таблица переходов хода из <c>task-manager.md</c> §4.2. Терминальные состояния не имеют
/// исходящих переходов; аудио у <see cref="TurnState.Completed"/> меняется не у хода, а у ответа.
/// </summary>
public static class TurnStateMachine
{
    /// <summary>Ход ещё обрабатывается и занимает сессию.</summary>
    public static bool IsActive(TurnState state) =>
        state is TurnState.Transcribing or TurnState.Interpreting or TurnState.Applying;

    /// <summary>Ход завершён и больше не меняет состояние.</summary>
    public static bool IsTerminal(TurnState state) =>
        state is TurnState.Completed
            or TurnState.NeedsClarification
            or TurnState.Failed
            or TurnState.Expired;

    /// <summary>
    /// Возвращает состояние после события или <see cref="TurnErrors.InvalidTransition"/>,
    /// если такого перехода в таблице нет.
    /// </summary>
    public static Result<TurnState> Next(TurnState from, TurnTransition transition)
    {
        if (!Enum.IsDefined(from))
        {
            return Result<TurnState>.Failure(TurnErrors.StateInvalid);
        }

        if (!Enum.IsDefined(transition))
        {
            return Result<TurnState>.Failure(TurnErrors.InvalidTransition);
        }

        TurnState? next = (from, transition) switch
        {
            (TurnState.Transcribing, TurnTransition.SttResult) => TurnState.Interpreting,
            (TurnState.Transcribing, TurnTransition.EmptyTranscript) => TurnState.Failed,
            (TurnState.Transcribing, TurnTransition.SttFailed) => TurnState.Failed,

            (TurnState.Interpreting, TurnTransition.ReadPlan) => TurnState.Completed,
            (TurnState.Interpreting, TurnTransition.DomainValidationFailed) => TurnState.NeedsClarification,
            (TurnState.Interpreting, TurnTransition.WritePlan) => TurnState.Applying,
            (TurnState.Interpreting, TurnTransition.ClarifyPlan) => TurnState.NeedsClarification,
            (TurnState.Interpreting, TurnTransition.NotUnderstood) => TurnState.Completed,
            (TurnState.Interpreting, TurnTransition.NluFailed) => TurnState.Failed,

            (TurnState.Applying, TurnTransition.CommandsApplied) => TurnState.Completed,
            (TurnState.Applying, TurnTransition.ApplyFailed) => TurnState.Failed,

            (_, TurnTransition.DeadlineExceeded) when IsActive(from) => TurnState.Expired,

            _ => null,
        };

        return next is null
            ? Result<TurnState>.Failure(TurnErrors.InvalidTransition)
            : Result<TurnState>.Success(next.Value);
    }

    /// <summary>Код ошибки, который фиксируется в <c>turns.error_code</c> после перехода.</summary>
    public static string? ErrorCodeFor(TurnTransition transition) =>
        transition switch
        {
            TurnTransition.EmptyTranscript => TurnErrors.EmptyTranscriptCode,
            TurnTransition.SttFailed => TurnErrors.SttFailedCode,
            TurnTransition.NluFailed => TurnErrors.NluFailedCode,
            TurnTransition.ApplyFailed => TurnErrors.DomainApplyFailedCode,
            TurnTransition.DeadlineExceeded => TurnErrors.DeadlineExceededCode,
            _ => null,
        };
}
