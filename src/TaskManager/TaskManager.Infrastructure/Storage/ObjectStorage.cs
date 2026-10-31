using System.Net.Http;
using TaskManager.Application.Abstractions;

namespace TaskManager.Infrastructure.Storage;

/// <summary>
/// S3-хранилище через presigned URL. Блобы не проходят через TaskManager;
/// <c>HeadObject</c> не вызывается — тип содержимого известен по расширению ключа.
/// </summary>
public sealed class ObjectStorage : IObjectStorage
{
    private readonly ObjectStorageOptions _options;
    private readonly TimeProvider _timeProvider;

    public ObjectStorage(ObjectStorageOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<PresignedUpload> CreatePresignedUploadAsync(
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        cancellationToken.ThrowIfCancellationRequested();

        string extension = ObjectContentTypes.ExtensionForContentType(contentType);
        string objectKey = $"audio/{Guid.NewGuid():N}{extension}";
        DateTimeOffset now = _timeProvider.GetUtcNow();

        Uri url = AwsV4Signer.CreatePresignedUrl(
            HttpMethod.Put.Method,
            _options.Endpoint,
            _options.AudioBucket,
            objectKey,
            _options.Region,
            _options.AccessKey,
            _options.SecretKey,
            now,
            _options.UploadTtl,
            responseContentType: null);

        return Task.FromResult(
            new PresignedUpload(objectKey, url, now.Add(_options.UploadTtl)));
    }

    public Task<Uri> CreatePresignedDownloadUrlAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        cancellationToken.ThrowIfCancellationRequested();

        string bucket = objectKey.StartsWith("tts/", StringComparison.Ordinal)
            ? _options.TtsBucket
            : _options.AudioBucket;

        DateTimeOffset now = _timeProvider.GetUtcNow();
        Uri url = AwsV4Signer.CreatePresignedUrl(
            HttpMethod.Get.Method,
            _options.Endpoint,
            bucket,
            objectKey,
            _options.Region,
            _options.AccessKey,
            _options.SecretKey,
            now,
            _options.DownloadTtl,
            ObjectContentTypes.FromObjectKey(objectKey));

        return Task.FromResult(url);
    }

    public string GetContentType(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        return ObjectContentTypes.FromObjectKey(objectKey);
    }

    private static void Validate(ObjectStorageOptions options)
    {
        if (!options.Endpoint.IsAbsoluteUri
            || (options.Endpoint.Scheme != Uri.UriSchemeHttp
                && options.Endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "S3 endpoint must be an absolute HTTP(S) URL.",
                nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.AccessKey)
            || string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new ArgumentException("S3 access key and secret key are required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.AudioBucket)
            || string.IsNullOrWhiteSpace(options.TtsBucket))
        {
            throw new ArgumentException("S3 bucket names are required.", nameof(options));
        }

        if (options.UploadTtl <= TimeSpan.Zero || options.DownloadTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Presigned URL TTL must be positive.");
        }
    }
}
