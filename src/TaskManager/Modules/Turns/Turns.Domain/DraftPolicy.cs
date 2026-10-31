namespace TaskManager.Turns.Domain;

/// <summary>
/// Правила жизни черновика. Черновик продолжает существовать только при интенте
/// <c>create_*</c> или <c>clarify</c>; на любом другом интенте он сбрасывается.
/// </summary>
public static class DraftPolicy
{
    /// <summary>
    /// <see langword="true"/>, если на этом интенте черновик нужно удалить.
    /// </summary>
    public static bool ShouldReset(TurnIntent intent) =>
        intent is not (
            TurnIntent.CreateTask
            or TurnIntent.CreateReminder
            or TurnIntent.CreateMeeting
            or TurnIntent.Clarify);
}
