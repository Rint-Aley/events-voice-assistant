namespace TaskManager.Turns.Domain.Tests;

public sealed class DomainCommandTests
{
    [Fact]
    public void Create_StartsPendingAndDueImmediately()
    {
        // Arrange
        // Act
        var result = DomainCommand.Create(
            Guid.NewGuid(),
            seq: 1,
            DomainCommandOperation.EventCreate,
            payload: "{\"kind\":\"task\"}",
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DomainCommandStatus.Pending);
        result.Value.Attempts.Should().Be(0);
        result.Value.NextAttemptAt.Should().Be(TestTurns.Now);
        result.Value.AppliedAt.Should().Be(default);
        result.Value.IsDue(TestTurns.Now).Should().BeTrue();
    }

    [Fact]
    public void MarkApplied_IsIdempotent()
    {
        // Arrange
        var command = DomainCommand.Create(
            Guid.NewGuid(),
            1,
            DomainCommandOperation.EventCreate,
            "{}",
            TestTurns.Now).Value;

        // Act
        var first = command.MarkApplied(TestTurns.Now.AddSeconds(1));
        var second = command.MarkApplied(TestTurns.Now.AddSeconds(2));

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        command.Status.Should().Be(DomainCommandStatus.Applied);
        command.AppliedAt.Should().Be(TestTurns.Now.AddSeconds(1));
        command.IsDue(TestTurns.Now.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void RegisterFailure_MarksCommandFailedAfterMaxAttempts()
    {
        // Arrange
        var command = DomainCommand.Create(
            Guid.NewGuid(),
            1,
            DomainCommandOperation.EventCreate,
            "{}",
            TestTurns.Now).Value;

        // Act
        var first = command.RegisterFailure(
            "timeout",
            TestTurns.Now.AddSeconds(1),
            TestTurns.Now.AddSeconds(1));
        var second = command.RegisterFailure(
            "timeout",
            TestTurns.Now.AddSeconds(2),
            TestTurns.Now.AddSeconds(2));
        var third = command.RegisterFailure(
            "timeout",
            TestTurns.Now.AddSeconds(3),
            TestTurns.Now.AddSeconds(3));

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        third.IsSuccess.Should().BeTrue();
        command.Attempts.Should().Be(DomainCommand.DefaultMaxAttempts);
        command.Status.Should().Be(DomainCommandStatus.Failed);
        command.LastError.Should().Be("timeout");
        command.NextAttemptAt.Should().Be(TestTurns.Now.AddSeconds(3));
    }

    [Fact]
    public void RegisterFailure_RejectsAppliedCommand()
    {
        // Arrange
        var command = DomainCommand.Create(
            Guid.NewGuid(),
            1,
            DomainCommandOperation.EventCreate,
            "{}",
            TestTurns.Now).Value;
        command.MarkApplied(TestTurns.Now.AddSeconds(1));

        // Act
        var result = command.RegisterFailure(
            "timeout",
            TestTurns.Now.AddSeconds(2),
            TestTurns.Now.AddSeconds(2));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DomainCommandErrors.StatusTransitionInvalid);
    }

    [Fact]
    public void Create_RequiresPayload()
    {
        // Arrange
        // Act
        var result = DomainCommand.Create(
            Guid.NewGuid(),
            1,
            DomainCommandOperation.EventCreate,
            "   ",
            TestTurns.Now);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(DomainCommandErrors.PayloadRequired);
    }
}
