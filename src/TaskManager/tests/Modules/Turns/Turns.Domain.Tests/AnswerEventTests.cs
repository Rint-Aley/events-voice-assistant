namespace TaskManager.Turns.Domain.Tests;

public sealed class AnswerEventTests
{
    [Fact]
    public void Create_LinksAnswerAndEventWithPosition()
    {
        // Arrange
        var answerId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        // Act
        var result = AnswerEvent.Create(answerId, eventId, position: 2);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AnswerId.Should().Be(answerId);
        result.Value.EventId.Should().Be(eventId);
        result.Value.Position.Should().Be(2);
    }

    [Fact]
    public void Create_RequiresAnswerId()
    {
        // Arrange
        // Act
        var result = AnswerEvent.Create(Guid.Empty, Guid.NewGuid());

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AnswerEventErrors.AnswerIdRequired);
    }

    [Fact]
    public void Create_RequiresEventId()
    {
        // Arrange
        // Act
        var result = AnswerEvent.Create(Guid.NewGuid(), Guid.Empty);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AnswerEventErrors.EventIdRequired);
    }

    [Fact]
    public void Create_RejectsNegativePosition()
    {
        // Arrange
        // Act
        var result = AnswerEvent.Create(Guid.NewGuid(), Guid.NewGuid(), position: -1);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(AnswerEventErrors.PositionInvalid);
    }
}
