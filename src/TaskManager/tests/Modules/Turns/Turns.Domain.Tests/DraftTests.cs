namespace TaskManager.Turns.Domain.Tests;

public sealed class DraftTests
{
    [Fact]
    public void Create_StartsWithEmptyClarificationHistory()
    {
        // Arrange
        var turnId = Guid.NewGuid();

        // Act
        var result = Draft.Create(
            turnId,
            TestTurns.Now,
            DraftKind.Task,
            description: "  Позвонить маме  ",
            sourceText: "  создай задачу  ");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TurnId.Should().Be(turnId);
        result.Value.Kind.Should().Be(DraftKind.Task);
        result.Value.Description.Should().Be("Позвонить маме");
        result.Value.SourceText.Should().Be("создай задачу");
        result.Value.ClarificationRound.Should().Be(0);
        result.Value.CanAskClarification.Should().BeTrue();
    }

    [Fact]
    public void AddSourceText_AppendsPhrasesThroughSemicolon()
    {
        // Arrange
        var draft = TestTurns.NewDraft(Guid.NewGuid());

        // Act
        var first = draft.AddSourceText("завтра", TestTurns.Now.AddSeconds(1));
        var second = draft.AddSourceText("в 10:00", TestTurns.Now.AddSeconds(2));

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        draft.SourceText.Should().Be("создай встречу; завтра; в 10:00");
        draft.UpdatedAt.Should().Be(TestTurns.Now.AddSeconds(2));
    }

    [Fact]
    public void AddSourceText_IgnoresBlankPhrase()
    {
        // Arrange
        var draft = TestTurns.NewDraft(Guid.NewGuid());

        // Act
        var result = draft.AddSourceText("   ", TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        draft.SourceText.Should().Be("создай встречу");
        draft.UpdatedAt.Should().Be(TestTurns.Now);
    }

    [Fact]
    public void BeginClarificationRound_AllowsTwoRounds()
    {
        // Arrange
        var draft = TestTurns.NewDraft(Guid.NewGuid());

        // Act
        var first = draft.BeginClarificationRound(TestTurns.Now.AddSeconds(1));
        var second = draft.BeginClarificationRound(TestTurns.Now.AddSeconds(2));

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        draft.ClarificationRound.Should().Be(Draft.MaxClarificationRounds);
        draft.CanAskClarification.Should().BeFalse();
    }

    [Fact]
    public void BeginClarificationRound_FailsAfterLimitAndKeepsRound()
    {
        // Arrange
        var draft = TestTurns.NewDraft(Guid.NewGuid(), clarificationRound: 2);

        // Act
        var result = draft.BeginClarificationRound(TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.ClarificationLimitExceeded);
        result.Error!.Code.Should().Be(DraftErrors.ClarificationLimitExceededCode);
        draft.ClarificationRound.Should().Be(2);
        draft.UpdatedAt.Should().Be(TestTurns.Now);
    }

    [Fact]
    public void Restore_RejectsClarificationRoundAboveLimit()
    {
        // Arrange
        // Act
        var result = Draft.Restore(
            Guid.NewGuid(),
            Guid.NewGuid(),
            clarificationRound: 3,
            createdAt: TestTurns.Now,
            updatedAt: TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.ClarificationRoundInvalid);
    }

    [Fact]
    public void Create_RequiresTurnId()
    {
        // Arrange
        // Act
        var result = Draft.Create(Guid.Empty, TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.TurnIdRequired);
    }

    [Fact]
    public void Create_RejectsTimeWithoutDate()
    {
        // Arrange
        // Act
        var result = Draft.Create(
            Guid.NewGuid(),
            TestTurns.Now,
            DraftKind.Task,
            startsAt: new TimeOnly(10, 0));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.TimeWithoutDate);
    }

    [Fact]
    public void Create_RejectsDeadlineForNonTask()
    {
        // Arrange
        // Act
        var result = Draft.Create(
            Guid.NewGuid(),
            TestTurns.Now,
            DraftKind.Meeting,
            startsOn: new DateOnly(2026, 10, 31),
            isDeadline: true);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.IsDeadlineOnlyForTask);
    }

    [Fact]
    public void UpdateSlots_ReplacesSlotsAndKeepsHistory()
    {
        // Arrange
        var draft = TestTurns.NewDraft(Guid.NewGuid());
        var date = new DateOnly(2026, 10, 31);
        var time = new TimeOnly(10, 0);

        // Act
        var result = draft.UpdateSlots(
            DraftKind.Meeting,
            "Обсудить релиз",
            startsOn: date,
            startsAt: time,
            duration: TimeSpan.FromMinutes(45),
            isDeadline: false,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        draft.Kind.Should().Be(DraftKind.Meeting);
        draft.Description.Should().Be("Обсудить релиз");
        draft.StartsOn.Should().Be(date);
        draft.StartsAt.Should().Be(time);
        draft.Duration.Should().Be(TimeSpan.FromMinutes(45));
        draft.SourceText.Should().Be("создай встречу");
        draft.UpdatedAt.Should().Be(TestTurns.Now.AddSeconds(1));
    }

    [Fact]
    public void UpdateSlots_CanClearInvalidTime()
    {
        // Arrange
        var draft = TestTurns.NewDraft(Guid.NewGuid());
        draft.UpdateSlots(
            DraftKind.Meeting,
            "Обсудить релиз",
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(10, 0),
            duration: TimeSpan.FromMinutes(60),
            isDeadline: false,
            TestTurns.Now.AddSeconds(1)).IsSuccess.Should().BeTrue();

        // Act
        var result = draft.UpdateSlots(
            DraftKind.Meeting,
            "Обсудить релиз",
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: null,
            duration: null,
            isDeadline: false,
            TestTurns.Now.AddSeconds(2));

        // Assert
        result.IsSuccess.Should().BeTrue();
        draft.StartsAt.Should().BeNull();
        draft.Duration.Should().BeNull();
    }

    [Fact]
    public void UpdateSlots_RejectsInvalidSlotsWithoutChanges()
    {
        // Arrange
        var draft = TestTurns.NewDraft(Guid.NewGuid());

        // Act
        var result = draft.UpdateSlots(
            DraftKind.Task,
            "Позвонить маме",
            startsOn: null,
            startsAt: new TimeOnly(10, 0),
            duration: null,
            isDeadline: false,
            TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.TimeWithoutDate);
        draft.Kind.Should().Be(DraftKind.Meeting);
        draft.StartsAt.Should().BeNull();
    }

    [Theory]
    [InlineData(DraftKind.Task)]
    [InlineData(DraftKind.Reminder)]
    public void Create_RejectsDurationForNonMeeting(DraftKind kind)
    {
        // Arrange
        // Act
        var result = Draft.Create(
            Guid.NewGuid(),
            TestTurns.Now,
            kind,
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(10, 0),
            duration: TimeSpan.FromMinutes(30));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.DurationOnlyForMeeting);
    }

    [Fact]
    public void Create_MeetingDurationRequiresTime()
    {
        // Arrange
        // Act
        var result = Draft.Create(
            Guid.NewGuid(),
            TestTurns.Now,
            DraftKind.Meeting,
            startsOn: new DateOnly(2026, 10, 31),
            duration: TimeSpan.FromMinutes(30));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.DurationRequiresTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_RejectsNonPositiveDuration(int minutes)
    {
        // Arrange
        // Act
        var result = Draft.Create(
            Guid.NewGuid(),
            TestTurns.Now,
            DraftKind.Meeting,
            startsOn: new DateOnly(2026, 10, 31),
            startsAt: new TimeOnly(10, 0),
            duration: TimeSpan.FromMinutes(minutes));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.DurationNotPositive);
    }

    [Fact]
    public void AddSourceText_StartsFromEmptyValueWithoutSeparator()
    {
        // Arrange
        var draft = Draft.Create(Guid.NewGuid(), TestTurns.Now).Value;

        // Act
        var result = draft.AddSourceText("создай задачу", TestTurns.Now.AddSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        draft.SourceText.Should().Be("создай задачу");
    }

    [Fact]
    public void Restore_RejectsNegativeClarificationRound()
    {
        // Arrange
        // Act
        var result = Draft.Restore(
            Guid.NewGuid(),
            Guid.NewGuid(),
            clarificationRound: -1,
            createdAt: TestTurns.Now,
            updatedAt: TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DraftErrors.ClarificationRoundInvalid);
    }

    [Theory]
    [InlineData(TurnIntent.CreateTask, false)]
    [InlineData(TurnIntent.CreateReminder, false)]
    [InlineData(TurnIntent.CreateMeeting, false)]
    [InlineData(TurnIntent.Clarify, false)]
    [InlineData(TurnIntent.ListPeriod, true)]
    [InlineData(TurnIntent.Search, true)]
    [InlineData(TurnIntent.Unknown, true)]
    public void DraftPolicy_ResetsOnlyForNonCreationIntents(TurnIntent intent, bool shouldReset)
    {
        // Arrange
        // Act
        var result = DraftPolicy.ShouldReset(intent);

        // Assert
        result.Should().Be(shouldReset);
    }
}
