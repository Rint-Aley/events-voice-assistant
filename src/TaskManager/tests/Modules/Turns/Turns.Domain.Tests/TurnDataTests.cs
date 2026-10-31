namespace TaskManager.Turns.Domain.Tests;

public sealed class TurnDataTests
{
    [Fact]
    public void ApplySttResult_StoresTextAndConfidence()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Transcribing);

        // Act
        var result = turn.ApplySttResult(
            "  создай задачу  ",
            confidence: 0.975m,
            turn.StateVersion,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        turn.Text.Should().Be("создай задачу");
        turn.SttConfidence.Should().Be(0.975m);
        turn.State.Should().Be(TurnState.Interpreting);
    }

    [Fact]
    public void ApplySttResult_RejectsEmptyTranscriptWithoutTransition()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Transcribing);

        // Act
        var result = turn.ApplySttResult(
            "   ",
            confidence: 0.9m,
            turn.StateVersion,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.TranscriptRequired);
        turn.State.Should().Be(TurnState.Transcribing);
        turn.StateVersion.Should().Be(Turn.InitialStateVersion);
        turn.Text.Should().BeNull();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ApplySttResult_RejectsConfidenceOutOfRange(double confidence)
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Transcribing);

        // Act
        var result = turn.ApplySttResult(
            "текст",
            (decimal)confidence,
            turn.StateVersion,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.SttConfidenceInvalid);
        turn.State.Should().Be(TurnState.Transcribing);
    }

    [Fact]
    public void ApplySttResult_RejectsTurnAlreadyInterpreting()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Interpreting);

        // Act
        var result = turn.ApplySttResult(
            "поздний результат",
            confidence: 0.9m,
            turn.StateVersion,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.InvalidTransition);
        turn.State.Should().Be(TurnState.Interpreting);
        turn.Text.Should().BeNull();
    }

    [Fact]
    public void ApplyPlan_StoresRawPlanForDebugging()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Interpreting);

        // Act
        var result = turn.ApplyPlan(
            TurnTransition.WritePlan,
            "  {\"action\":\"create_task\"}  ",
            turn.StateVersion,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        turn.Plan.Should().Be("{\"action\":\"create_task\"}");
        turn.State.Should().Be(TurnState.Applying);
    }

    [Fact]
    public void ApplyPlan_RejectsTransitionThatDoesNotBelongToInterpretation()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Applying);

        // Act
        var result = turn.ApplyPlan(
            TurnTransition.CommandsApplied,
            "{}",
            turn.StateVersion,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.InvalidTransition);
        turn.State.Should().Be(TurnState.Applying);
        turn.Plan.Should().BeNull();
    }
}
