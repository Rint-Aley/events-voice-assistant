namespace TaskManager.Turns.Domain.Tests;

public sealed class TurnDraftTests
{
    [Fact]
    public void AttachDraft_StoresSingleDraft()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);
        var draft = TestTurns.NewDraft(turn.Id);

        // Act
        var result = turn.AttachDraft(draft);

        // Assert
        result.IsSuccess.Should().BeTrue();
        turn.Draft.Should().BeSameAs(draft);
    }

    [Fact]
    public void AttachDraft_RejectsSecondDraftForSameTurn()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);
        turn.AttachDraft(TestTurns.NewDraft(turn.Id)).IsSuccess.Should().BeTrue();

        // Act
        var result = turn.AttachDraft(TestTurns.NewDraft(turn.Id));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.DraftAlreadyExists);
    }

    [Fact]
    public void AttachDraft_RejectsDraftOfAnotherTurn()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);
        var foreignDraft = TestTurns.NewDraft(Guid.NewGuid());

        // Act
        var result = turn.AttachDraft(foreignDraft);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.DraftBelongsToAnotherTurn);
        turn.Draft.Should().BeNull();
    }

    [Theory]
    [InlineData(TurnState.Transcribing)]
    [InlineData(TurnState.Interpreting)]
    [InlineData(TurnState.Applying)]
    [InlineData(TurnState.Completed)]
    [InlineData(TurnState.Failed)]
    [InlineData(TurnState.Expired)]
    public void AttachDraft_RequiresClarificationState(TurnState state)
    {
        // Arrange
        var turn = TestTurns.InState(state);
        var draft = TestTurns.NewDraft(turn.Id);

        // Act
        var result = turn.AttachDraft(draft);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.DraftRequiresClarificationState);
        turn.Draft.Should().BeNull();
    }

    [Fact]
    public void AttachDraft_RejectsNull()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);

        // Act
        var act = () => turn.AttachDraft(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CancelDraft_RemovesDraftAndKeepsTurnState()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);
        turn.AttachDraft(TestTurns.NewDraft(turn.Id)).IsSuccess.Should().BeTrue();

        // Act
        var result = turn.CancelDraft();

        // Assert
        result.IsSuccess.Should().BeTrue();
        turn.Draft.Should().BeNull();
        turn.State.Should().Be(TurnState.NeedsClarification);
        turn.StateVersion.Should().Be(Turn.InitialStateVersion);
    }

    [Fact]
    public void CancelDraft_IsIdempotent()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);
        turn.AttachDraft(TestTurns.NewDraft(turn.Id)).IsSuccess.Should().BeTrue();
        turn.CancelDraft().IsSuccess.Should().BeTrue();

        // Act
        var secondCancel = turn.CancelDraft();

        // Assert
        secondCancel.IsSuccess.Should().BeTrue();
        turn.Draft.Should().BeNull();
    }

    [Fact]
    public void CancelDraft_WithoutDraftIsNotAnError()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);

        // Act
        var result = turn.CancelDraft();

        // Assert
        result.IsSuccess.Should().BeTrue();
        turn.Draft.Should().BeNull();
    }

    [Theory]
    [InlineData(TurnIntent.CreateTask)]
    [InlineData(TurnIntent.CreateReminder)]
    [InlineData(TurnIntent.CreateMeeting)]
    [InlineData(TurnIntent.Clarify)]
    public void TryResetDraftFor_KeepsDraftOnContinuationIntent(TurnIntent intent)
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);
        var draft = TestTurns.NewDraft(turn.Id);
        turn.AttachDraft(draft).IsSuccess.Should().BeTrue();

        // Act
        var result = turn.TryResetDraftFor(intent);

        // Assert
        result.Should().BeFalse();
        turn.Draft.Should().BeSameAs(draft);
    }

    [Theory]
    [InlineData(TurnIntent.ListPeriod)]
    [InlineData(TurnIntent.Search)]
    [InlineData(TurnIntent.Unknown)]
    public void TryResetDraftFor_ResetsDraftOnOtherIntent(TurnIntent intent)
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);
        turn.AttachDraft(TestTurns.NewDraft(turn.Id)).IsSuccess.Should().BeTrue();

        // Act
        var result = turn.TryResetDraftFor(intent);

        // Assert
        result.Should().BeTrue();
        turn.Draft.Should().BeNull();
        turn.State.Should().Be(TurnState.NeedsClarification);
    }

    [Fact]
    public void TryResetDraftFor_WithoutDraftReturnsFalse()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.NeedsClarification);

        // Act
        var result = turn.TryResetDraftFor(TurnIntent.Search);

        // Assert
        result.Should().BeFalse();
        turn.Draft.Should().BeNull();
    }
}
