using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TaskManager.Infrastructure.Storage;

/// <summary>
/// Подпись presigned URL по AWS Signature Version 4. Клиенты S3 используют
/// path-style addressing; <c>Host</c> входит в подпись и не переписывается после неё.
/// </summary>
internal static class AwsV4Signer
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Service = "s3";
    private const string Terminal = "aws4_request";
    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    public static Uri CreatePresignedUrl(
        string method,
        Uri endpoint,
        string bucket,
        string objectKey,
        string region,
        string accessKey,
        string secretKey,
        DateTimeOffset now,
        TimeSpan ttl,
        string? responseContentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        long expiresIn = Math.Max(1, (long)ttl.TotalSeconds);
        DateTime utc = now.UtcDateTime;
        string amzDate = utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string dateStamp = utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        string canonicalUri = BuildCanonicalPath(endpoint.AbsolutePath, bucket, objectKey);
        string host = endpoint.Authority.ToLowerInvariant();

        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = Algorithm,
            ["X-Amz-Credential"] = $"{accessKey}/{dateStamp}/{region}/{Service}/{Terminal}",
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = expiresIn.ToString(CultureInfo.InvariantCulture),
            ["X-Amz-SignedHeaders"] = "host",
        };

        if (!string.IsNullOrWhiteSpace(responseContentType))
        {
            query["response-content-type"] = responseContentType;
        }

        var encodedQuery = query
            .Select(pair => new KeyValuePair<string, string>(
                Encode(pair.Key),
                Encode(pair.Value)))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        string canonicalQuery = string.Join(
            "&",
            encodedQuery.Select(pair => $"{pair.Key}={pair.Value}"));

        string canonicalRequest = string.Join(
            "\n",
            method.ToUpperInvariant(),
            canonicalUri,
            canonicalQuery,
            $"host:{host}\n",
            "host",
            UnsignedPayload);

        string stringToSign = string.Join(
            "\n",
            Algorithm,
            amzDate,
            $"{dateStamp}/{region}/{Service}/{Terminal}",
            ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        byte[] signingKey = GetSigningKey(secretKey, dateStamp, region);
        string signature = ToHex(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

        string baseUrl = $"{endpoint.Scheme}://{host}{canonicalUri}";
        string url = $"{baseUrl}?{canonicalQuery}&X-Amz-Signature={signature}";

        return new Uri(url, UriKind.Absolute);
    }

    private static string BuildCanonicalPath(string basePath, string bucket, string objectKey)
    {
        string raw = $"{basePath.TrimEnd('/')}/{bucket}/{objectKey}";
        if (!raw.StartsWith('/'))
        {
            raw = $"/{raw}";
        }

        return string.Join(
            "/",
            raw.Split('/').Select(Encode));
    }

    private static string Encode(string value) =>
        Uri.EscapeDataString(value).Replace("%7E", "~", StringComparison.Ordinal);

    private static byte[] GetSigningKey(string secretKey, string dateStamp, string region)
    {
        byte[] dateKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes($"AWS4{secretKey}"),
            Encoding.UTF8.GetBytes(dateStamp));

        byte[] regionKey = HMACSHA256.HashData(dateKey, Encoding.UTF8.GetBytes(region));
        byte[] serviceKey = HMACSHA256.HashData(regionKey, Encoding.UTF8.GetBytes(Service));

        return HMACSHA256.HashData(serviceKey, Encoding.UTF8.GetBytes(Terminal));
    }

    private static string ToHex(byte[] value) => Convert.ToHexStringLower(value);
}
