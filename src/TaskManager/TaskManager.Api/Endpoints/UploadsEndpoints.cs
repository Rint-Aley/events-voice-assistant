using TaskManager.Api.Contracts;
using TaskManager.Application.Abstractions;

namespace TaskManager.Api.Endpoints;

/// <summary>Приём аудио: проверка контракта загрузки и presigned PUT.</summary>
public static class UploadsEndpoints
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm",
        "audio/ogg",
        "audio/wav",
        "audio/mpeg",
    };

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/uploads", Create);
    }

    private static async Task<IResult> Create(
        HttpRequest request,
        IObjectStorage storage,
        CancellationToken cancellationToken)
    {
        JsonReadResult<UploadRequest> read = await ApiResults.ReadJsonAsync<UploadRequest>(
            request,
            cancellationToken);

        if (read.Error is not null)
        {
            return read.Error;
        }

        UploadRequest body = read.Value!;
        string? contentType = body.ContentType?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(contentType))
        {
            return ApiResults.BadRequest("content_type обязателен");
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            return ApiResults.Error(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.UnsupportedAudio,
                "Недопустимый формат аудио");
        }

        if (body.SizeBytes is null)
        {
            return ApiResults.BadRequest("size_bytes обязателен");
        }

        if (body.SizeBytes < 0)
        {
            return ApiResults.BadRequest("size_bytes не может быть отрицательным");
        }

        if (body.SizeBytes > ApiLimits.MaxUploadBytes)
        {
            return ApiResults.Error(
                StatusCodes.Status413PayloadTooLarge,
                ApiErrorCodes.PayloadTooLarge,
                $"Файл больше {ApiLimits.MaxUploadMegabytes}");
        }

        PresignedUpload upload = await storage.CreatePresignedUploadAsync(
            contentType,
            cancellationToken);

        return ApiResults.Json(
            new UploadResponse(upload.ObjectKey, upload.Url.ToString(), upload.ExpiresAt),
            StatusCodes.Status201Created);
    }
}
