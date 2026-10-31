using TaskManager.Application.Abstractions;
using TaskManager.Infrastructure.Storage;

namespace TaskManager.IntegrationTests.Infrastructure;

public sealed class ObjectStorageTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("audio/voice.webm", "audio/webm")]
    [InlineData("audio/voice.ogg", "audio/ogg")]
    [InlineData("tts/voice.opus", "audio/ogg")]
    [InlineData("audio/voice.wav", "audio/wav")]
    [InlineData("audio/voice.mp3", "audio/mpeg")]
    [InlineData("audio/voice.unknown", "application/octet-stream")]
    [InlineData("audio/voice", "application/octet-stream")]
    public void ContentType_is_derived_from_object_key_extension(
        string objectKey,
        string expected)
    {
        // Arrange — подготовки нет: правило зависит только от расширения ключа.

        // Act
        string contentType = ObjectContentTypes.FromObjectKey(objectKey);

        // Assert
        contentType.Should().Be(expected);
    }

    [Fact]
    public async Task CreatePresignedUploadAsync_builds_signed_put_with_upload_ttl()
    {
        // Arrange
        ObjectStorage storage = CreateStorage();

        // Act
        PresignedUpload upload = await storage.CreatePresignedUploadAsync("audio/webm");

        // Assert
        upload.ObjectKey.Should().MatchRegex("^audio/[0-9a-f]{32}\\.webm$");
        upload.ExpiresAt.Should().Be(Now.AddSeconds(600));
        upload.Url.Host.Should().Be("localhost");
        upload.Url.Port.Should().Be(9000);
        upload.Url.AbsolutePath.Should().Be($"/audio/{upload.ObjectKey}");

        Dictionary<string, string> query = ParseQuery(upload.Url);
        query["X-Amz-Algorithm"].Should().Be("AWS4-HMAC-SHA256");
        query["X-Amz-Credential"].Should().Be("todo-s3/20261030/us-east-1/s3/aws4_request");
        query["X-Amz-Date"].Should().Be("20261030T120000Z");
        query["X-Amz-Expires"].Should().Be("600");
        query["X-Amz-SignedHeaders"].Should().Be("host");
        query["X-Amz-Signature"].Should().MatchRegex("^[0-9a-f]{64}$");
        query.Should().NotContainKey("response-content-type");
    }

    [Theory]
    // expectedPath = /{bucket}/{key}: ключ tts/... попадает в бакет tts, поэтому видны два префикса.
    [InlineData("tts/0123456789abcdef.opus", "/tts/tts/0123456789abcdef.opus", "audio/ogg")]
    [InlineData("audio/voice.dat", "/audio/audio/voice.dat", "application/octet-stream")]
    public async Task CreatePresignedDownloadUrlAsync_uses_key_extension_and_download_ttl(
        string objectKey,
        string expectedPath,
        string expectedContentType)
    {
        // Arrange
        ObjectStorage storage = CreateStorage();

        // Act
        Uri url = await storage.CreatePresignedDownloadUrlAsync(objectKey);

        // Assert
        url.AbsolutePath.Should().Be(expectedPath);

        Dictionary<string, string> query = ParseQuery(url);
        query["X-Amz-Expires"].Should().Be("900");
        query["response-content-type"].Should().Be(expectedContentType);
        query["X-Amz-Signature"].Should().MatchRegex("^[0-9a-f]{64}$");
    }

    private static ObjectStorage CreateStorage() =>
        new(
            new ObjectStorageOptions(
                new Uri("http://localhost:9000"),
                "todo-s3",
                "todo-s3-secret",
                "audio",
                "tts",
                TimeSpan.FromSeconds(600),
                TimeSpan.FromSeconds(900)),
            new FixedTimeProvider(Now));

    private static Dictionary<string, string> ParseQuery(Uri uri) =>
        uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : string.Empty));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
