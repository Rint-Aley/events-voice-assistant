namespace TaskManager.Turns.Domain.Tests;

public sealed class TurnTransitionTests
{
    private static readonly (TurnState From, TurnTransition Transition, TurnState To)[] AllowedTransitions =
    {
        (TurnState.Transcribing, TurnTransition.SttResult, TurnState.Interpreting),
        (TurnState.Transcribing, TurnTransition.EmptyTranscript, TurnState.Failed),
        (TurnState.Transcribing, TurnTransition.SttFailed, TurnState.Failed),
        (TurnState.Transcribing, TurnTransition.DeadlineExceeded, TurnState.Expired),

        (TurnState.Interpreting, TurnTransition.ReadPlan, TurnState.Completed),
        (TurnState.Interpreting, TurnTransition.DomainValidationFailed, TurnState.NeedsClarification),
        (TurnState.Interpreting, TurnTransition.WritePlan, TurnState.Applying),
        (TurnState.Interpreting, TurnTransition.ClarifyPlan, TurnState.NeedsClarification),
        (TurnState.Interpreting, TurnTransition.NotUnderstood, TurnState.Completed),
        (TurnState.Interpreting, TurnTransition.NluFailed, TurnState.Failed),
        (TurnState.Interpreting, TurnTransition.DeadlineExceeded, TurnState.Expired),

        (TurnState.Applying, TurnTransition.CommandsApplied, TurnState.Completed),
        (TurnState.Applying, TurnTransition.ApplyFailed, TurnState.Failed),
        (TurnState.Applying, TurnTransition.DeadlineExceeded, TurnState.Expired),
    };

    public static IEnumerable<object[]> AllStateTransitionCombinations()
    {
        foreach (var state in Enum.GetValues<TurnState>())
        {
            foreach (var transition in Enum.GetValues<TurnTransition>())
            {
                yield return new object[] { state, transition };
            }
        }
    }

    public static IEnumerable<object[]> AllowedFailureTransitions()
    {
        return AllowedTransitions
            .Where(item => TurnStateMachine.ErrorCodeFor(item.Transition) is not null)
            .Select(item => new object[]
            {
                item.From,
                item.Transition,
                TurnStateMachine.ErrorCodeFor(item.Transition)!,
            });
    }

    [Theory]
    [MemberData(nameof(AllStateTransitionCombinations))]
    public void Transition_IsAllowedOnlyByTable(TurnState from, TurnTransition transition)
    {
        // Arrange
        var turn = TestTurns.InState(from);
        var expected = ExpectedTarget(from, transition);

        // Act
        var result = turn.Transition(transition, turn.StateVersion, TestTurns.Now.AddSeconds(1));

        // Assert
        if (expected is null)
        {
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Be(TurnErrors.InvalidTransition);
            turn.State.Should().Be(from);
            turn.StateVersion.Should().Be(Turn.InitialStateVersion);
            turn.UpdatedAt.Should().Be(TestTurns.Now);
        }
        else
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().Be(expected.Value);
            turn.State.Should().Be(expected.Value);
            turn.StateVersion.Should().Be(Turn.InitialStateVersion + 1);
            turn.UpdatedAt.Should().Be(TestTurns.Now.AddSeconds(1));
        }
    }

    [Theory]
    [MemberData(nameof(AllowedFailureTransitions))]
    public void FailureTransition_SetsErrorCode(
        TurnState from,
        TurnTransition transition,
        string expectedCode)
    {
        // Arrange
        var turn = TestTurns.InState(from);

        // Act
        var result = turn.Transition(transition, turn.StateVersion, TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        turn.ErrorCode.Should().Be(expectedCode);
        turn.State.Should().BeOneOf(TurnState.Failed, TurnState.Expired);
    }

    [Fact]
    public void SuccessfulTransition_ClearsStaleErrorCode()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Interpreting, errorCode: "STALE");

        // Act
        var result = turn.Transition(
            TurnTransition.ReadPlan,
            turn.StateVersion,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        turn.State.Should().Be(TurnState.Completed);
        turn.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void TerminalTurn_RejectsDeadlineTransition()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Completed);

        // Act
        var result = turn.Transition(
            TurnTransition.DeadlineExceeded,
            turn.StateVersion,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.InvalidTransition);
        turn.State.Should().Be(TurnState.Completed);
        turn.StateVersion.Should().Be(Turn.InitialStateVersion);
    }

    private static TurnState? ExpectedTarget(TurnState from, TurnTransition transition) =>
        AllowedTransitions
            .Where(item => item.From == from && item.Transition == transition)
            .Select(item => (TurnState?)item.To)
            .SingleOrDefault();
}
