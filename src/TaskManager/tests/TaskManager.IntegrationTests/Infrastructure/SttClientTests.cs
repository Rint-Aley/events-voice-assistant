using System.Net;
using System.Net.Http;
using System.Text.Json;
using TaskManager.Infrastructure.Clients;
using TaskManager.Turns.Application.Clients.Stt;

namespace TaskManager.IntegrationTests.Infrastructure;

public sealed class SttClientTests
{
    [Fact]
    public async Task TranscribeAsync_posts_contract_and_maps_response()
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        var audioUrl = new Uri("http://localhost:9000/audio/voice.webm?X-Amz-Signature=abc");
        (SttClient client, RecordingHttpMessageHandler handler) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                $$"""
                {
                  "turn_id": "{{turnId}}",
                  "text": "купить хлеб",
                  "language": "ru",
                  "confidence": 0.93,
                  "empty": false,
                  "duration_ms": 1500,
                  "provider": "whisper",
                  "model": "large-v3"
                }
                """)));

        // Act
        SttResult result = await client.TranscribeAsync(
            new SttRequest(turnId, audioUrl, "audio/webm", "ru", 60000));

        // Assert
        result.TurnId.Should().Be(turnId);
        result.Text.Should().Be("купить хлеб");
        result.Language.Should().Be("ru");
        result.Confidence.Should().Be(0.93m);
        result.Empty.Should().BeFalse();
        result.DurationMs.Should().Be(1500);
        result.Provider.Should().Be("whisper");
        result.Model.Should().Be("large-v3");

        RecordedRequest request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("http://stt.local/v1/transcribe"));

        using JsonDocument body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("turn_id").GetString().Should().Be(turnId.ToString());
        body.RootElement.GetProperty("audio_url").GetString().Should().Be(audioUrl.ToString());
        body.RootElement.GetProperty("content_type").GetString().Should().Be("audio/webm");
        body.RootElement.GetProperty("language").GetString().Should().Be("ru");
        body.RootElement.GetProperty("max_duration_ms").GetInt32().Should().Be(60000);
    }

    [Theory]
    [InlineData(400, true, "AUDIO_TOO_LONG", false)]
    [InlineData(503, false, "PROVIDER_UNAVAILABLE", true)]
    [InlineData(504, true, "PROVIDER_TIMEOUT", false)]
    public async Task TranscribeAsync_maps_http_failure_to_permanent_or_retryable(
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
        (SttClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(body, statusCode)));

        // Act
        Func<Task> act = () => client.TranscribeAsync(CreateRequest());

        // Assert
        SttException error = (await act.Should().ThrowAsync<SttException>()).Which;
        error.StatusCode.Should().Be(statusCode);
        error.IsPermanent.Should().Be(isPermanent);
        error.Message.Should().Contain(code);
    }

    [Fact]
    public async Task TranscribeAsync_timeout_is_retryable()
    {
        // Arrange
        (SttClient client, _) = CreateClient(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            timeout: TimeSpan.FromMilliseconds(100));

        // Act
        Func<Task> act = () => client.TranscribeAsync(CreateRequest());

        // Assert
        SttException error = (await act.Should().ThrowAsync<SttException>()).Which;
        error.StatusCode.Should().BeNull();
        error.IsPermanent.Should().BeFalse();
        error.InnerException.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task TranscribeAsync_invalid_response_is_permanent()
    {
        // Arrange
        (SttClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json("{ not-json }")));

        // Act
        Func<Task> act = () => client.TranscribeAsync(CreateRequest());

        // Assert
        SttException error = (await act.Should().ThrowAsync<SttException>()).Which;
        error.IsPermanent.Should().BeTrue();
        error.InnerException.Should().BeOfType<JsonException>();
    }

    [Fact]
    public async Task TranscribeAsync_empty_transcript_with_empty_text_is_valid()
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        (SttClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                Response(turnId, empty: true, text: string.Empty))));

        // Act
        SttResult result = await client.TranscribeAsync(
            new SttRequest(turnId, new Uri("http://localhost:9000/audio/voice.webm"), "audio/webm", "ru"));

        // Assert
        result.Empty.Should().BeTrue();
        result.Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(true, "непустой текст")]
    public async Task TranscribeAsync_text_contradicting_empty_is_permanent(
        bool empty,
        string? text)
    {
        // Arrange
        Guid turnId = Guid.NewGuid();
        (SttClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                Response(turnId, empty, text))));

        // Act
        Func<Task> act = () => client.TranscribeAsync(
            new SttRequest(turnId, new Uri("http://localhost:9000/audio/voice.webm"), "audio/webm", "ru"));

        // Assert
        SttException error = (await act.Should().ThrowAsync<SttException>()).Which;
        error.IsPermanent.Should().BeTrue();
        error.Message.Should().Contain("does not match the contract");
    }

    [Fact]
    public async Task TranscribeAsync_response_for_another_turn_is_permanent()
    {
        // Arrange
        (SttClient client, _) = CreateClient(
            (_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
                Response(Guid.NewGuid(), empty: false, text: "купить хлеб"))));

        // Act
        Func<Task> act = () => client.TranscribeAsync(CreateRequest());

        // Assert
        SttException error = (await act.Should().ThrowAsync<SttException>()).Which;
        error.IsPermanent.Should().BeTrue();
        error.Message.Should().Contain("does not match the contract");
    }

    private static string Response(Guid turnId, bool empty, string? text) =>
        JsonSerializer.Serialize(new
        {
            turn_id = turnId,
            text,
            language = "ru",
            confidence = 0.9,
            empty,
            duration_ms = 1000,
            provider = "whisper",
            model = "large-v3",
        });

    private static SttRequest CreateRequest() =>
        new(Guid.NewGuid(), new Uri("http://localhost:9000/audio/voice.webm"), "audio/webm", "ru");

    private static (SttClient Client, RecordingHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        TimeSpan? timeout = null)
    {
        var handler = new RecordingHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://stt.local/"),
        };

        return (
            new SttClient(httpClient, new SttClientOptions(timeout ?? TimeSpan.FromSeconds(1))),
            handler);
    }
}
