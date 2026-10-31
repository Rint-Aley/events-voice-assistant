using System.Net;
using System.Net.Http;
using System.Text.Json;
using TaskManager.Infrastructure.Clients;
using TaskManager.Turns.Application.Clients.Tts;

namespace TaskManager.IntegrationTests.Infrastructure;

public sealed class TtsClientTests
{
    [Fact]
    public async Task SynthesizeAsync_posts_contract_and_maps_response()
    {
        // Arrange
        Guid answerId = Guid.NewGuid();
        (TtsClient client, RecordingHttpMessageHandler handler) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "answer_id": "{{answerId}}",
                  "object_key": "tts/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.opus",
                  "content_type": "audio/ogg",
                  "format": "ogg_opus",
                  "duration_ms": 2400,
                  "cached": true
                }
                """)));

        // Act
        TtsResult result = await client.SynthesizeAsync(
            new TtsRequest(answerId, "Записала задачу.", "anna", "ogg_opus", 1.1));

        // Assert
        result.AnswerId.Should().Be(answerId);
        result.ObjectKey.Should().StartWith("tts/").And.EndWith(".opus");
        result.ContentType.Should().Be("audio/ogg");
        result.Format.Should().Be("ogg_opus");
        result.DurationMs.Should().Be(2400);
        result.Cached.Should().BeTrue();

        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("http://tts.local/v1/synthesize"));

        using JsonDocument body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("answer_id").GetString().Should().Be(answerId.ToString());
        body.RootElement.GetProperty("text").GetString().Should().Be("Записала задачу.");
        body.RootElement.GetProperty("voice").GetString().Should().Be("anna");
        body.RootElement.GetProperty("format").GetString().Should().Be("ogg_opus");
        body.RootElement.GetProperty("speed").GetDouble().Should().Be(1.1);
    }

    [Theory]
    [InlineData(400, true, "BAD_REQUEST", false)]
    [InlineData(503, false, "PROVIDER_UNAVAILABLE", true)]
    [InlineData(503, true, "PROVIDER_UNAVAILABLE", false)]
    public async Task SynthesizeAsync_maps_http_failure_to_permanent_or_retryable(
        int statusCode,
        bool isPermanent,
        string code,
        bool retryable)
    {
        // Arrange
        string body = JsonSerializer.Serialize(new
        {
            error = new { code, message = "failed", retryable },
        });
        (TtsClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(body, statusCode)));

        // Act
        Func<Task> act = () => client.SynthesizeAsync(CreateRequest());

        // Assert
        TtsException error = (await act.Should().ThrowAsync<TtsException>()).Which;
        error.StatusCode.Should().Be(statusCode);
        error.IsPermanent.Should().Be(isPermanent);
        error.Message.Should().Contain(code);
    }

    [Fact]
    public async Task SynthesizeAsync_invalid_response_is_permanent()
    {
        // Arrange
        (TtsClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json("{ not-json }")));

        // Act
        Func<Task> act = () => client.SynthesizeAsync(CreateRequest());

        // Assert
        TtsException error = (await act.Should().ThrowAsync<TtsException>()).Which;
        error.IsPermanent.Should().BeTrue();
        error.InnerException.Should().BeOfType<JsonException>();
    }

    [Fact]
    public async Task SynthesizeAsync_timeout_is_retryable()
    {
        // Arrange
        (TtsClient client, _) = CreateClient(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            timeout: TimeSpan.FromMilliseconds(100));

        // Act
        Func<Task> act = () => client.SynthesizeAsync(CreateRequest());

        // Assert
        TtsException error = (await act.Should().ThrowAsync<TtsException>()).Which;
        error.StatusCode.Should().BeNull();
        error.IsPermanent.Should().BeFalse();
        error.InnerException.Should().BeAssignableTo<OperationCanceledException>();
    }

    private static TtsRequest CreateRequest() =>
        new(Guid.NewGuid(), "Записала задачу.", "anna", "ogg_opus");

    private static (TtsClient Client, RecordingHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        TimeSpan? timeout = null)
    {
        var handler = new RecordingHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://tts.local/"),
        };

        return (
            new TtsClient(httpClient, new TtsClientOptions(timeout ?? TimeSpan.FromSeconds(1))),
            handler);
    }
}
