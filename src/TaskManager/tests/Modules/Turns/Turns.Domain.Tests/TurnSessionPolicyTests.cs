namespace TaskManager.Turns.Domain.Tests;

public sealed class TurnSessionPolicyTests
{
    [Theory]
    [InlineData(TurnState.Transcribing)]
    [InlineData(TurnState.Interpreting)]
    [InlineData(TurnState.Applying)]
    public void EnsureCanStart_RejectsSecondActiveTurnInSession(TurnState activeState)
    {
        // Arrange
        var activeTurn = TestTurns.InState(activeState);

        // Act
        var result = TurnSessionPolicy.EnsureCanStart(
            TestTurns.SessionId,
            new[] { activeTurn });

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.SessionBusy);
        result.Error!.Code.Should().Be(TurnErrors.SessionBusyCode);
    }

    [Theory]
    [InlineData(TurnState.Completed)]
    [InlineData(TurnState.NeedsClarification)]
    [InlineData(TurnState.Failed)]
    [InlineData(TurnState.Expired)]
    public void EnsureCanStart_AllowsNewTurnAfterTerminalTurn(TurnState terminalState)
    {
        // Arrange
        var terminalTurn = TestTurns.InState(terminalState);

        // Act
        var result = TurnSessionPolicy.EnsureCanStart(
            TestTurns.SessionId,
            new[] { terminalTurn });

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureCanStart_IgnoresActiveTurnInAnotherSession()
    {
        // Arrange
        var otherTurn = TestTurns.InState(TurnState.Transcribing);

        // Act
        var result = TurnSessionPolicy.EnsureCanStart(
            Guid.NewGuid(),
            new[] { otherTurn });

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureCanStart_RejectsBlankSessionId()
    {
        // Arrange
        // Act
        var result = TurnSessionPolicy.EnsureCanStart(
            Guid.Empty,
            Array.Empty<Turn>());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.SessionIdRequired);
    }

    [Fact]
    public void EnsureCanStart_RequiresTurnCollection()
    {
        // Arrange
        // Act
        var act = () => TurnSessionPolicy.EnsureCanStart(TestTurns.SessionId, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
