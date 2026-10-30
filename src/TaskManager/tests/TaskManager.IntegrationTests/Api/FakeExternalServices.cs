using TaskManager.Application.Abstractions;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Ports;

namespace TaskManager.IntegrationTests.Api;

/// <summary>STT-заглушка: отдаёт заранее заданный результат и запоминает запросы.</summary>
public sealed class FakeSttClient : ISttClient
{
    private readonly List<SttRequest> _requests = new();

    public Func<SttRequest, SttResult>? Respond { get; set; }

    public IReadOnlyList<SttRequest> Requests => _requests;

    public void Reset()
    {
        _requests.Clear();
        Respond = null;
    }

    public Task<SttResult> TranscribeAsync(
        SttRequest request,
        CancellationToken cancellationToken = default)
    {
        _requests.Add(request);

        if (Respond is null)
        {
            throw new InvalidOperationException("STT response is not configured.");
        }

        return Task.FromResult(Respond(request));
    }
}

/// <summary>S3-заглушка: presigned ссылки формируются локально, сеть не нужна.</summary>
public sealed class FakeObjectStorage : IObjectStorage
{
    public const string UploadUrl = "http://s3.local/audio/presigned-put";
    public const string DownloadUrl = "http://s3.local/tts/presigned-get";

    public bool FailPresign { get; set; }

    public void Reset() => FailPresign = false;

    public Task<PresignedUpload> CreatePresignedUploadAsync(
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (FailPresign)
        {
            throw new InvalidOperationException("S3 is unavailable.");
        }

        return Task.FromResult(new PresignedUpload(
            "audio/fake.webm",
            new Uri(UploadUrl),
            DateTimeOffset.UtcNow.AddMinutes(10)));
    }

    public Task<Uri> CreatePresignedDownloadUrlAsync(
        string objectKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new Uri($"{DownloadUrl}?key={Uri.EscapeDataString(objectKey)}"));

    public string GetContentType(string objectKey) => "audio/webm";
}
