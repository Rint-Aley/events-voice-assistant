using TaskManager.Turns.Application.Clients.Tts;
using TaskManager.Turns.Application.Ports;

namespace TaskManager.IntegrationTests.Turns;

/// <summary>TTS-заглушка: запоминает вызовы и не выполняет синтез сама по себе.</summary>
public sealed class FakeTtsClient : ITtsClient
{
    private readonly List<TtsRequest> _requests = new();

    public IReadOnlyList<TtsRequest> Requests => _requests;

    public Func<TtsRequest, TtsResult>? Respond { get; set; }

    public void Reset()
    {
        _requests.Clear();
        Respond = null;
    }

    public Task<TtsResult> SynthesizeAsync(
        TtsRequest request,
        CancellationToken cancellationToken = default)
    {
        _requests.Add(request);

        if (Respond is null)
        {
            throw new InvalidOperationException("TTS response is not configured.");
        }

        return Task.FromResult(Respond(request));
    }
}
