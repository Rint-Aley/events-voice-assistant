using TaskManager.Turns.Application.Clients.Nlu;
using TaskManager.Turns.Application.Ports;

namespace TaskManager.IntegrationTests.Turns;

/// <summary>Подменный <c>nlu</c>: отдаёт заранее заданный ответ и запоминает запросы.</summary>
internal sealed class FakeNluClient : INluClient
{
    private readonly List<NluRequest> _requests = new();

    public Func<NluRequest, NluResponse>? Respond { get; set; }

    public IReadOnlyList<NluRequest> Requests => _requests;

    public Task<NluResponse> InterpretAsync(
        NluRequest request,
        CancellationToken cancellationToken = default)
    {
        _requests.Add(request);

        NluResponse response = Respond is null
            ? throw new InvalidOperationException("FakeNluClient.Respond is not configured.")
            : Respond(request);

        return Task.FromResult(
            response.TurnId == Guid.Empty
                ? response with { TurnId = request.TurnId }
                : response);
    }
}
