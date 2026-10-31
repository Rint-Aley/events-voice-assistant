namespace TaskManager.Turns.Domain.Tests;

public sealed class TurnCreationTests
{
    [Fact]
    public void Create_VoiceTurnStartsTranscribing()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid();
        var deadlineAt = TestTurns.Now.AddSeconds(90);

        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            TurnState.Transcribing,
            idempotencyKey,
            deadlineAt,
            TestTurns.Now,
            audioObjectKey: "audio/turn.webm");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(TurnState.Transcribing);
        result.Value.StateVersion.Should().Be(Turn.InitialStateVersion);
        result.Value.SessionId.Should().Be(TestTurns.SessionId);
        result.Value.IdempotencyKey.Should().Be(idempotencyKey);
        result.Value.DeadlineAt.Should().Be(deadlineAt);
        result.Value.CreatedAt.Should().Be(TestTurns.Now);
        result.Value.UpdatedAt.Should().Be(TestTurns.Now);
        result.Value.AudioObjectKey.Should().Be("audio/turn.webm");
        result.Value.Text.Should().BeNull();
        result.Value.ErrorCode.Should().BeNull();
        result.Value.Draft.Should().BeNull();
    }

    [Fact]
    public void Create_TextTurnStartsInterpretingAndStoresText()
    {
        // Arrange
        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            TurnState.Interpreting,
            Guid.NewGuid(),
            TestTurns.Now.AddSeconds(90),
            TestTurns.Now,
            text: "  создай задачу  ");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(TurnState.Interpreting);
        result.Value.Text.Should().Be("создай задачу");
        result.Value.AudioObjectKey.Should().BeNull();
    }

    [Fact]
    public void Create_VoiceTurnRequiresAudioObjectKey()
    {
        // Arrange
        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            TurnState.Transcribing,
            Guid.NewGuid(),
            TestTurns.Now.AddSeconds(90),
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.AudioObjectKeyRequired);
    }

    [Fact]
    public void Create_TextTurnRequiresText()
    {
        // Arrange
        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            TurnState.Interpreting,
            Guid.NewGuid(),
            TestTurns.Now.AddSeconds(90),
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.TranscriptRequired);
    }

    [Fact]
    public void Create_VoiceTurnRejectsEnteredText()
    {
        // Arrange
        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            TurnState.Transcribing,
            Guid.NewGuid(),
            TestTurns.Now.AddSeconds(90),
            TestTurns.Now,
            audioObjectKey: "audio/turn.webm",
            text: "создай задачу");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.TextNotExpected);
    }

    [Fact]
    public void Create_TextTurnRejectsAudioObjectKey()
    {
        // Arrange
        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            TurnState.Interpreting,
            Guid.NewGuid(),
            TestTurns.Now.AddSeconds(90),
            TestTurns.Now,
            audioObjectKey: "audio/turn.webm",
            text: "создай задачу");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.AudioObjectKeyNotExpected);
    }

    [Theory]
    [InlineData(TurnState.Applying)]
    [InlineData(TurnState.Completed)]
    [InlineData(TurnState.NeedsClarification)]
    [InlineData(TurnState.Failed)]
    [InlineData(TurnState.Expired)]
    public void Create_RejectsNonInitialState(TurnState initialState)
    {
        // Arrange
        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            initialState,
            Guid.NewGuid(),
            TestTurns.Now.AddSeconds(90),
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.InitialStateInvalid);
    }

    [Fact]
    public void Create_RequiresSessionId()
    {
        // Arrange
        // Act
        var result = Turn.Create(
            Guid.Empty,
            TurnState.Transcribing,
            Guid.NewGuid(),
            TestTurns.Now.AddSeconds(90),
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.SessionIdRequired);
    }

    [Fact]
    public void Create_RequiresIdempotencyKey()
    {
        // Arrange
        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            TurnState.Transcribing,
            Guid.Empty,
            TestTurns.Now.AddSeconds(90),
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.IdempotencyKeyRequired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RequiresFutureDeadline(int seconds)
    {
        // Arrange
        // Act
        var result = Turn.Create(
            TestTurns.SessionId,
            TurnState.Transcribing,
            Guid.NewGuid(),
            TestTurns.Now.AddSeconds(seconds),
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.DeadlineInvalid);
    }

    [Fact]
    public void Restore_RejectsNonPositiveStateVersion()
    {
        // Arrange
        // Act
        var result = Turn.Restore(
            Guid.NewGuid(),
            TestTurns.SessionId,
            TurnState.Interpreting,
            stateVersion: 0,
            idempotencyKey: Guid.NewGuid(),
            deadlineAt: TestTurns.Now.AddSeconds(90),
            createdAt: TestTurns.Now.AddMinutes(-1),
            updatedAt: TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.StateVersionInvalid);
    }

    [Fact]
    public void Restore_RejectsUpdatedAtBeforeCreatedAt()
    {
        // Arrange
        // Act
        var result = Turn.Restore(
            Guid.NewGuid(),
            TestTurns.SessionId,
            TurnState.Interpreting,
            stateVersion: 1,
            idempotencyKey: Guid.NewGuid(),
            deadlineAt: TestTurns.Now.AddSeconds(90),
            createdAt: TestTurns.Now,
            updatedAt: TestTurns.Now.AddSeconds(-1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.TimestampInvalid);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Restore_RejectsConfidenceOutOfRange(double confidence)
    {
        // Arrange
        // Act
        var result = Turn.Restore(
            Guid.NewGuid(),
            TestTurns.SessionId,
            TurnState.Interpreting,
            stateVersion: 1,
            idempotencyKey: Guid.NewGuid(),
            deadlineAt: TestTurns.Now.AddSeconds(90),
            createdAt: TestTurns.Now,
            updatedAt: TestTurns.Now,
            sttConfidence: (decimal)confidence);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.SttConfidenceInvalid);
    }

    [Fact]
    public void Restore_KeepsStateVersionAndDraft()
    {
        // Arrange
        var turnId = Guid.NewGuid();
        var draft = TestTurns.NewDraft(turnId, clarificationRound: 1);

        // Act
        var result = Turn.Restore(
            turnId,
            TestTurns.SessionId,
            TurnState.NeedsClarification,
            stateVersion: 4,
            idempotencyKey: Guid.NewGuid(),
            deadlineAt: TestTurns.Now.AddSeconds(90),
            createdAt: TestTurns.Now.AddMinutes(-1),
            updatedAt: TestTurns.Now,
            draft: draft);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(TurnState.NeedsClarification);
        result.Value.StateVersion.Should().Be(4);
        result.Value.Draft.Should().BeSameAs(draft);
    }

    [Fact]
    public void Restore_RejectsDraftOnNonClarificationTurn()
    {
        // Arrange
        var turnId = Guid.NewGuid();
        var draft = TestTurns.NewDraft(turnId);

        // Act
        var result = Turn.Restore(
            turnId,
            TestTurns.SessionId,
            TurnState.Completed,
            stateVersion: 2,
            idempotencyKey: Guid.NewGuid(),
            deadlineAt: TestTurns.Now.AddSeconds(90),
            createdAt: TestTurns.Now.AddMinutes(-1),
            updatedAt: TestTurns.Now,
            draft: draft);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.DraftRequiresClarificationState);
    }

    [Fact]
    public void Restore_RejectsDraftFromAnotherTurn()
    {
        // Arrange
        var draft = TestTurns.NewDraft(Guid.NewGuid());

        // Act
        var result = Turn.Restore(
            Guid.NewGuid(),
            TestTurns.SessionId,
            TurnState.NeedsClarification,
            stateVersion: 2,
            idempotencyKey: Guid.NewGuid(),
            deadlineAt: TestTurns.Now.AddSeconds(90),
            createdAt: TestTurns.Now.AddMinutes(-1),
            updatedAt: TestTurns.Now,
            draft: draft);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(TurnErrors.DraftBelongsToAnotherTurn);
    }
}
