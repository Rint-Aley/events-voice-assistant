namespace TaskManager.Infrastructure.Storage;

/// <summary>
/// Настройки S3 и presigned-ссылок. <c>UploadTtl</c>/<c>DownloadTtl</c> соответствуют
/// <c>S3_PRESIGN_TTL_UPLOAD</c> (600 с) и <c>S3_PRESIGN_TTL_DOWNLOAD</c> (900 с).
/// <see cref="Endpoint"/> — публичный адрес (<c>S3_PUBLIC_HOST:S3_PUBLIC_PORT</c>),
/// который попадает в подпись.
/// </summary>
public sealed record ObjectStorageOptions(
    Uri Endpoint,
    string AccessKey,
    string SecretKey,
    string AudioBucket,
    string TtsBucket,
    TimeSpan UploadTtl,
    TimeSpan DownloadTtl,
    string Region = "us-east-1");
