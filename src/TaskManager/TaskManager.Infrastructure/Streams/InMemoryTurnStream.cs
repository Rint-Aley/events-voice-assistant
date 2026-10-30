using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TaskManager.Application.Abstractions;

namespace TaskManager.Infrastructure.Streams;

/// <summary>
/// In-memory хаб подписок на события хода. Подходит для одной реплики: события не
/// переживают перезапуск и не рассылаются между экземплярами. История не хранится —
/// снапшот при подключении отдаёт Api.
/// </summary>
public sealed class InMemoryTurnStream : ITurnStream
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, List<Channel<TurnStreamEvent>>> _subscribers = new();

    public ValueTask PublishAsync(
        TurnStreamEvent streamEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);
        cancellationToken.ThrowIfCancellationRequested();

        Channel<TurnStreamEvent>[] targets;
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(streamEvent.TurnId, out List<Channel<TurnStreamEvent>>? channels)
                || channels.Count == 0)
            {
                return ValueTask.CompletedTask;
            }

            targets = channels.ToArray();
        }

        foreach (Channel<TurnStreamEvent> channel in targets)
        {
            // Подписчик мог отписаться между снимком и записью: событие для него уже не нужно.
            channel.Writer.TryWrite(streamEvent);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<TurnStreamEvent> SubscribeAsync(
        Guid turnId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channel = Channel.CreateUnbounded<TurnStreamEvent>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

        lock (_gate)
        {
            if (!_subscribers.TryGetValue(turnId, out List<Channel<TurnStreamEvent>>? channels))
            {
                channels = new List<Channel<TurnStreamEvent>>();
                _subscribers[turnId] = channels;
            }

            channels.Add(channel);
        }

        try
        {
            await foreach (TurnStreamEvent streamEvent in channel.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return streamEvent;
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_subscribers.TryGetValue(turnId, out List<Channel<TurnStreamEvent>>? channels))
                {
                    channels.Remove(channel);
                    if (channels.Count == 0)
                    {
                        _subscribers.Remove(turnId);
                    }
                }
            }

            channel.Writer.TryComplete();
        }
    }
}
