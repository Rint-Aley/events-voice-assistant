using TaskManager.Application.Abstractions;
using TaskManager.Infrastructure.Streams;

namespace TaskManager.IntegrationTests.Infrastructure;

public sealed class InMemoryTurnStreamTests
{
    [Fact]
    public async Task PublishAsync_delivers_event_to_subscriber()
    {
        // Arrange
        var stream = new InMemoryTurnStream();
        Guid turnId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<TurnStreamEvent> subscriber = stream
            .SubscribeAsync(turnId, cancellation.Token)
            .GetAsyncEnumerator();

        ValueTask<bool> next = subscriber.MoveNextAsync();
        await Task.Yield();

        // Act
        await stream.PublishAsync(TurnStreamEvent.StateChanged(turnId, "INTERPRETING"));

        // Assert
        (await next).Should().BeTrue();
        subscriber.Current.TurnId.Should().Be(turnId);
        subscriber.Current.Kind.Should().Be(TurnStreamEventKind.State);
        subscriber.Current.State.Should().Be("INTERPRETING");
    }

    [Fact]
    public async Task PublishAsync_delivers_event_to_every_subscriber_of_turn()
    {
        // Arrange
        var stream = new InMemoryTurnStream();
        Guid turnId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<TurnStreamEvent> first = stream
            .SubscribeAsync(turnId, cancellation.Token)
            .GetAsyncEnumerator();
        await using IAsyncEnumerator<TurnStreamEvent> second = stream
            .SubscribeAsync(turnId, cancellation.Token)
            .GetAsyncEnumerator();

        ValueTask<bool> firstNext = first.MoveNextAsync();
        ValueTask<bool> secondNext = second.MoveNextAsync();
        await Task.Yield();

        // Act
        await stream.PublishAsync(TurnStreamEvent.AnswerReady(turnId));

        // Assert
        (await firstNext).Should().BeTrue();
        (await secondNext).Should().BeTrue();
        first.Current.Kind.Should().Be(TurnStreamEventKind.Answer);
        second.Current.Kind.Should().Be(TurnStreamEventKind.Answer);
    }

    [Fact]
    public async Task PublishAsync_does_not_deliver_events_of_another_turn()
    {
        // Arrange
        var stream = new InMemoryTurnStream();
        Guid turnId = Guid.NewGuid();
        Guid otherTurnId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<TurnStreamEvent> subscriber = stream
            .SubscribeAsync(turnId, cancellation.Token)
            .GetAsyncEnumerator();

        ValueTask<bool> next = subscriber.MoveNextAsync();
        await Task.Yield();

        // Act
        await stream.PublishAsync(TurnStreamEvent.StateChanged(otherTurnId, "APPLYING"));
        await stream.PublishAsync(TurnStreamEvent.AudioReady(turnId));

        // Assert
        (await next).Should().BeTrue();
        subscriber.Current.TurnId.Should().Be(turnId);
        subscriber.Current.Kind.Should().Be(TurnStreamEventKind.Audio);
    }

    [Fact]
    public async Task PublishAsync_without_subscribers_is_noop()
    {
        // Arrange
        var stream = new InMemoryTurnStream();

        // Act
        Func<Task> act = async () =>
            await stream.PublishAsync(TurnStreamEvent.AnswerReady(Guid.NewGuid()));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SubscribeAsync_stops_when_cancelled()
    {
        // Arrange
        var stream = new InMemoryTurnStream();
        Guid turnId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        await using IAsyncEnumerator<TurnStreamEvent> subscriber = stream
            .SubscribeAsync(turnId, cancellation.Token)
            .GetAsyncEnumerator();

        ValueTask<bool> next = subscriber.MoveNextAsync();
        await Task.Yield();

        // Act
        cancellation.Cancel();
        Func<Task> act = async () => await next;

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        Func<Task> publish = async () =>
            await stream.PublishAsync(TurnStreamEvent.AnswerReady(turnId));
        await publish.Should().NotThrowAsync();
    }
}
