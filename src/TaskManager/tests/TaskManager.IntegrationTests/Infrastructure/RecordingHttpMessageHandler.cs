using System.Net.Http;
using System.Text;

namespace TaskManager.IntegrationTests.Infrastructure;

/// <summary>
/// Подменный HTTP-обработчик: запоминает запросы и позволяет задать ответ или ошибку
/// без сетевого вызова.
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _respond = respond;
    }

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    private readonly List<RecordedRequest> _requests = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        _requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));

        return await _respond(request, cancellationToken);
    }

    public static HttpResponseMessage Json(string json, int statusCode = 200) =>
        new((System.Net.HttpStatusCode)statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}

internal sealed record RecordedRequest(HttpMethod Method, Uri? Uri, string? Body);
