namespace TaskManager.Turns.Domain.Tests;

public sealed class AnswerTests
{
    [Fact]
    public void Create_DefaultsToPendingTts()
    {
        // Arrange
        // Act
        var result = Answer.Create(Guid.NewGuid(), "Готово", TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AudioStatus.Should().Be(AnswerAudioStatus.Pending);
        result.Value.AudioObjectKey.Should().BeNull();
        result.Value.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Create_CanSkipTtsExplicitly()
    {
        // Arrange
        // Act
        var result = Answer.Create(
            Guid.NewGuid(),
            "Готово",
            TestTurns.Now,
            audioStatus: AnswerAudioStatus.Skipped);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AudioStatus.Should().Be(AnswerAudioStatus.Skipped);
    }

    [Fact]
    public void MarkAudioReady_WritesKeyDurationAndStatus()
    {
        // Arrange
        var answer = Answer.Create(
            Guid.NewGuid(),
            "Готово",
            TestTurns.Now,
            audioStatus: AnswerAudioStatus.Pending).Value;

        // Act
        var result = answer.MarkAudioReady("tts/answer.mp3", 6100);

        // Assert
        result.IsSuccess.Should().BeTrue();
        answer.AudioStatus.Should().Be(AnswerAudioStatus.Ready);
        answer.AudioObjectKey.Should().Be("tts/answer.mp3");
        answer.AudioDurationMs.Should().Be(6100);
    }

    [Fact]
    public void MarkAudioFailed_IsIdempotent()
    {
        // Arrange
        var answer = Answer.Create(
            Guid.NewGuid(),
            "Готово",
            TestTurns.Now,
            audioStatus: AnswerAudioStatus.Pending).Value;

        // Act
        var first = answer.MarkAudioFailed();
        var second = answer.MarkAudioFailed();

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        answer.AudioStatus.Should().Be(AnswerAudioStatus.Failed);
        answer.AudioObjectKey.Should().BeNull();
    }

    [Fact]
    public void MarkAudioReady_RejectsAnswerWithoutScheduledTts()
    {
        // Arrange
        var answer = Answer.Create(
            Guid.NewGuid(),
            "Готово",
            TestTurns.Now,
            audioStatus: AnswerAudioStatus.Skipped).Value;

        // Act
        var result = answer.MarkAudioReady("tts/answer.mp3", 1000);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AnswerErrors.AudioStatusTransitionInvalid);
        answer.AudioStatus.Should().Be(AnswerAudioStatus.Skipped);
    }

    [Fact]
    public void MarkAudioReady_AllowsZeroDurationFromTtsContract()
    {
        // Arrange
        var answer = Answer.Create(
            Guid.NewGuid(),
            "Готово",
            TestTurns.Now,
            audioStatus: AnswerAudioStatus.Pending).Value;

        // Act
        var result = answer.MarkAudioReady("tts/answer.mp3", 0);

        // Assert
        result.IsSuccess.Should().BeTrue();
        answer.AudioStatus.Should().Be(AnswerAudioStatus.Ready);
        answer.AudioDurationMs.Should().Be(0);
    }

    [Fact]
    public void MarkAudioReady_RejectsNegativeDuration()
    {
        // Arrange
        var answer = Answer.Create(
            Guid.NewGuid(),
            "Готово",
            TestTurns.Now,
            audioStatus: AnswerAudioStatus.Pending).Value;

        // Act
        var result = answer.MarkAudioReady("tts/answer.mp3", -1);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AnswerErrors.AudioDurationInvalid);
    }

    [Fact]
    public void AudioUpdate_DoesNotChangeCompletedTurn()
    {
        // Arrange
        var turn = TestTurns.InState(TurnState.Completed);
        var answer = Answer.Create(
            turn.Id,
            "Готово",
            TestTurns.Now,
            audioStatus: AnswerAudioStatus.Pending).Value;

        // Act
        var result = answer.MarkAudioReady("tts/answer.mp3", 6100);

        // Assert
        result.IsSuccess.Should().BeTrue();
        turn.State.Should().Be(TurnState.Completed);
        turn.StateVersion.Should().Be(Turn.InitialStateVersion);
        answer.AudioStatus.Should().Be(AnswerAudioStatus.Ready);
    }

    [Fact]
    public void Create_RequiresText()
    {
        // Arrange
        // Act
        var result = Answer.Create(Guid.NewGuid(), "   ", TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AnswerErrors.TextRequired);
    }

    [Fact]
    public void Create_RejectsAlreadyReadyAudio()
    {
        // Arrange
        // Act
        var result = Answer.Create(
            Guid.NewGuid(),
            "Готово",
            TestTurns.Now,
            audioStatus: AnswerAudioStatus.Ready);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AnswerErrors.AudioStatusTransitionInvalid);
    }

    [Fact]
    public void Restore_ReadyAudioRequiresObjectKey()
    {
        // Arrange
        // Act
        var result = Answer.Restore(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Готово",
            AnswerAudioStatus.Ready,
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AnswerErrors.AudioObjectKeyRequired);
    }

    [Fact]
    public void Create_NormalizesWarningsAndTotalCount()
    {
        // Arrange
        // Act
        var result = Answer.Create(
            Guid.NewGuid(),
            "Список",
            TestTurns.Now,
            totalCount: 5,
            warnings: new[] { " Пересечение ", "   " });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(5);
        result.Value.Warnings.Should().Equal("Пересечение");
    }
}
