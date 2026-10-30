namespace TaskManager.Turns.Domain.Tests;

public sealed class TurnCasTests
{
    [Fact]
    public void Transition_WithStaleVersion_IsRejectedWithoutChanges()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Transcribing, stateVersion: 5);
        var staleVersion = turn.StateVersion - 1;
        var now = TestTurns.Now.AddSeconds(1);

        // Act
        var result = turn.Transition(TurnTransition.SttResult, staleVersion, now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.StateVersionConflict);
        turn.State.Should().Be(TurnState.Transcribing);
        turn.StateVersion.Should().Be(5);
        turn.UpdatedAt.Should().Be(TestTurns.Now);
    }

    [Fact]
    public void Transition_WithFutureVersion_IsRejectedAsConflict()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Interpreting, stateVersion: 3);

        // Act
        var result = turn.Transition(
            TurnTransition.ReadPlan,
            expectedStateVersion: 4,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.StateVersionConflict);
        turn.State.Should().Be(TurnState.Interpreting);
        turn.StateVersion.Should().Be(3);
    }

    [Fact]
    public void ConcurrentTransition_OnlyFirstAttemptWins()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Transcribing);
        var expectedVersion = turn.StateVersion;

        // Act
        var first = turn.Transition(
            TurnTransition.SttResult,
            expectedVersion,
            TestTurns.Now.AddSeconds(1));
        var second = turn.Transition(
            TurnTransition.SttResult,
            expectedVersion,
            TestTurns.Now.AddSeconds(2));

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Be(TurnErrors.StateVersionConflict);
        turn.State.Should().Be(TurnState.Interpreting);
        turn.StateVersion.Should().Be(expectedVersion + 1);
        turn.UpdatedAt.Should().Be(TestTurns.Now.AddSeconds(1));
    }

    [Fact]
    public void LateStaleResult_AfterExpiry_IsDiscarded()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Transcribing);
        var expectedVersion = turn.StateVersion;
        var expired = turn.Transition(
            TurnTransition.DeadlineExceeded,
            expectedVersion,
            TestTurns.Now.AddSeconds(90));
        expired.IsSuccess.Should().BeTrue();

        // Act
        var lateResult = turn.Transition(
            TurnTransition.SttResult,
            expectedVersion,
            TestTurns.Now.AddSeconds(91));

        // Assert
        lateResult.IsSuccess.Should().BeFalse();
        lateResult.Error.Should().Be(TurnErrors.StateVersionConflict);
        turn.State.Should().Be(TurnState.Expired);
        turn.ErrorCode.Should().Be(TurnErrors.DeadlineExceededCode);
    }

    [Fact]
    public void EachSuccessfulTransition_IncrementsVersion()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Transcribing);

        // Act
        var first = turn.Transition(TurnTransition.SttResult, 1, TestTurns.Now.AddSeconds(1));
        var second = turn.Transition(TurnTransition.WritePlan, 2, TestTurns.Now.AddSeconds(2));
        var third = turn.Transition(TurnTransition.CommandsApplied, 3, TestTurns.Now.AddSeconds(3));

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        third.IsSuccess.Should().BeTrue();
        turn.State.Should().Be(TurnState.Completed);
        turn.StateVersion.Should().Be(4);
    }
}
