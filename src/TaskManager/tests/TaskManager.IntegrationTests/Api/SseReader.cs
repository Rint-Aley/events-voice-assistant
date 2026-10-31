using System.Text;

namespace TaskManager.IntegrationTests.Api;

public sealed record SseFrame(string? Event, string? Data, string? Comment);

/// <summary>Минимальный разбор SSE-потока для проверки порядка кадров и ping.</summary>
public static class SseReader
{
    public static async Task<List<SseFrame>> ReadAsync(
        StreamReader reader,
        int count,
        CancellationToken cancellationToken)
    {
        var frames = new List<SseFrame>(count);
        string? eventName = null;
        var data = new StringBuilder();

        while (frames.Count < count)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith(':'))
            {
                // Keep-alive ping не является кадром события: тесты сравнивают только события.
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line[5..].TrimStart());
                continue;
            }

            if (line.Length == 0 && eventName is not null)
            {
                frames.Add(new SseFrame(eventName, data.ToString(), null));
                eventName = null;
                data.Clear();
            }
        }

        return frames;
    }

    public static async Task<SseFrame?> ReadUntilAsync(
        StreamReader reader,
        Func<SseFrame, bool> predicate,
        CancellationToken cancellationToken)
    {
        string? eventName = null;
        var data = new StringBuilder();

        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return null;
            }

            if (line.StartsWith(':'))
            {
                var comment = new SseFrame(null, null, line[1..].Trim());
                if (predicate(comment))
                {
                    return comment;
                }

                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Append(line[5..].TrimStart());
                continue;
            }

            if (line.Length == 0 && eventName is not null)
            {
                var frame = new SseFrame(eventName, data.ToString(), null);
                eventName = null;
                data.Clear();

                if (predicate(frame))
                {
                    return frame;
                }
            }
        }
    }
}
