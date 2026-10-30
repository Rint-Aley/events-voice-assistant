namespace TaskManager.Turns.Domain.Tests;

internal static class TestTurns
{
    public static readonly DateTimeOffset Now = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

    public static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static Turn InState(
        TurnState state,
        int stateVersion = Turn.InitialStateVersion,
        string? errorCode = null)
    {
        var result = Turn.Restore(
            id: Guid.NewGuid(),
            sessionId: SessionId,
            state: state,
            stateVersion: stateVersion,
            idempotencyKey: Guid.NewGuid(),
            deadlineAt: Now.AddSeconds(90),
            createdAt: Now.AddMinutes(-1),
            updatedAt: Now,
            errorCode: errorCode);

        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }

    public static Draft NewDraft(Guid turnId, int clarificationRound = 0)
    {
        var result = clarificationRound == 0
            ? Draft.Create(
                turnId,
                Now,
                DraftKind.Meeting,
                description: "Обсудить релиз",
                sourceText: "создай встречу")
            : Draft.Restore(
                Guid.NewGuid(),
                turnId,
                clarificationRound,
                createdAt: Now.AddMinutes(-1),
                updatedAt: Now,
                kind: DraftKind.Meeting,
                description: "Обсудить релиз",
                sourceText: "создай встречу");

        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }
}
