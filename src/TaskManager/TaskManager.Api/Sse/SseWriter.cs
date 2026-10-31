using System.Text;
using System.Text.Json;
using TaskManager.Api.Contracts;

namespace TaskManager.Api.Sse;

/// <summary>
/// Запись кадров <c>text/event-stream</c>. После каждого кадра буфер сбрасывается,
/// иначе прокси и браузер не увидят <c>answer</c> раньше <c>audio</c>.
/// </summary>
public sealed class SseWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;

    public SseWriter(Stream body)
    {
        _writer = new StreamWriter(body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false,
            NewLine = "\n",
        };
    }

    public async Task WriteEventAsync(
        string eventName,
        object data,
        CancellationToken cancellationToken)
    {
        await _writer.WriteAsync($"event: {eventName}\n".AsMemory(), cancellationToken);
        await _writer.WriteAsync("data: ".AsMemory(), cancellationToken);
        await _writer.WriteAsync(
            JsonSerializer.Serialize(data, ApiJson.Options).AsMemory(),
            cancellationToken);
        await _writer.WriteAsync("\n\n".AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
    }

    public async Task WriteCommentAsync(string comment, CancellationToken cancellationToken)
    {
        await _writer.WriteAsync($": {comment}\n\n".AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() => await _writer.DisposeAsync();
}
