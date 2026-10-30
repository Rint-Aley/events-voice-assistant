using TaskManager.SharedKernel;

namespace TaskManager.Turns.Domain;

/// <summary>
/// Ответ хода. Текст и ссылка на события отдаются сразу, аудио дописывается позже и не
/// меняет состояние хода.
/// </summary>
public sealed class Answer
{
    private Answer()
    {
        Text = null!;
        Warnings = Array.Empty<string>();
    }

    private Answer(
        Guid id,
        Guid turnId,
        string text,
        int? totalCount,
        IReadOnlyList<string> warnings,
        string? audioObjectKey,
        AnswerAudioStatus audioStatus,
        int? audioDurationMs,
        DateTimeOffset createdAt)
    {
        Id = id;
        TurnId = turnId;
        Text = text;
        TotalCount = totalCount;
        Warnings = warnings;
        AudioObjectKey = audioObjectKey;
        AudioStatus = audioStatus;
        AudioDurationMs = audioDurationMs;
        CreatedAt = createdAt;
    }

    /// <summary>Идентификатор ответа.</summary>
    public Guid Id { get; }

    /// <summary>Ход, которому принадлежит ответ; связь один к одному.</summary>
    public Guid TurnId { get; }

    /// <summary>Текст ответа.</summary>
    public string Text { get; }

    /// <summary>Сколько записей подходит под период до лимита; только для списка.</summary>
    public int? TotalCount { get; }

    /// <summary>Предупреждения, например пересечения встреч.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Ключ аудио в S3.</summary>
    public string? AudioObjectKey { get; private set; }

    /// <summary>Статус озвучки.</summary>
    public AnswerAudioStatus AudioStatus { get; private set; }

    /// <summary>Длительность озвучки в миллисекундах.</summary>
    public int? AudioDurationMs { get; private set; }

    /// <summary>Время создания ответа.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Создаёт ответ. По умолчанию озвучка ставится в очередь
    /// (<see cref="AnswerAudioStatus.Pending"/>), потому что TTS синтезируется всегда в фоне.
    /// </summary>
    public static Result<Answer> Create(
        Guid turnId,
        string? text,
        DateTimeOffset now,
        int? totalCount = null,
        IEnumerable<string>? warnings = null,
        AnswerAudioStatus audioStatus = AnswerAudioStatus.Pending,
        Guid? id = null)
    {
        if (audioStatus is not (AnswerAudioStatus.Skipped or AnswerAudioStatus.Pending))
        {
            return Result<Answer>.Failure(AnswerErrors.AudioStatusTransitionInvalid);
        }

        return Build(
            id ?? Guid.NewGuid(),
            turnId,
            text,
            totalCount,
            warnings,
            audioObjectKey: null,
            audioStatus,
            audioDurationMs: null,
            createdAt: now);
    }

    /// <summary>Восстанавливает сохранённый ответ из хранилища.</summary>
    public static Result<Answer> Restore(
        Guid id,
        Guid turnId,
        string? text,
        AnswerAudioStatus audioStatus,
        DateTimeOffset createdAt,
        int? totalCount = null,
        IEnumerable<string>? warnings = null,
        string? audioObjectKey = null,
        int? audioDurationMs = null)
    {
        return Build(
            id,
            turnId,
            text,
            totalCount,
            warnings,
            audioObjectKey,
            audioStatus,
            audioDurationMs,
            createdAt);
    }

    /// <summary>Помечает ответ как ожидающий синтеза; повторная постановка идемпотентна.</summary>
    public Result MarkAudioPending()
    {
        if (AudioStatus is not (AnswerAudioStatus.Skipped or AnswerAudioStatus.Pending))
        {
            return Result.Failure(AnswerErrors.AudioStatusTransitionInvalid);
        }

        AudioStatus = AnswerAudioStatus.Pending;

        return Result.Success();
    }

    /// <summary>Записывает готовое аудио. Допустимо только из <see cref="AnswerAudioStatus.Pending"/>.</summary>
    public Result MarkAudioReady(string? audioObjectKey, int audioDurationMs)
    {
        if (AudioStatus != AnswerAudioStatus.Pending)
        {
            return Result.Failure(AnswerErrors.AudioStatusTransitionInvalid);
        }

        if (string.IsNullOrWhiteSpace(audioObjectKey))
        {
            return Result.Failure(AnswerErrors.AudioObjectKeyRequired);
        }

        if (audioDurationMs <= 0)
        {
            return Result.Failure(AnswerErrors.AudioDurationInvalid);
        }

        AudioObjectKey = audioObjectKey.Trim();
        AudioDurationMs = audioDurationMs;
        AudioStatus = AnswerAudioStatus.Ready;

        return Result.Success();
    }

    /// <summary>
    /// Помечает озвучку неудачной. Ошибка <c>tts</c> не переводит ход в
    /// <see cref="TurnState.Failed"/>; повторная отметка идемпотентна.
    /// </summary>
    public Result MarkAudioFailed()
    {
        if (AudioStatus is not (AnswerAudioStatus.Pending or AnswerAudioStatus.Failed))
        {
            return Result.Failure(AnswerErrors.AudioStatusTransitionInvalid);
        }

        AudioStatus = AnswerAudioStatus.Failed;

        return Result.Success();
    }

    private static Result<Answer> Build(
        Guid id,
        Guid turnId,
        string? text,
        int? totalCount,
        IEnumerable<string>? warnings,
        string? audioObjectKey,
        AnswerAudioStatus audioStatus,
        int? audioDurationMs,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            return Result<Answer>.Failure(AnswerErrors.IdRequired);
        }

        if (turnId == Guid.Empty)
        {
            return Result<Answer>.Failure(AnswerErrors.TurnIdRequired);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result<Answer>.Failure(AnswerErrors.TextRequired);
        }

        if (totalCount < 0)
        {
            return Result<Answer>.Failure(AnswerErrors.TotalCountInvalid);
        }

        if (!Enum.IsDefined(audioStatus))
        {
            return Result<Answer>.Failure(AnswerErrors.AudioStatusInvalid);
        }

        if (audioDurationMs <= 0)
        {
            return Result<Answer>.Failure(AnswerErrors.AudioDurationInvalid);
        }

        if (audioStatus == AnswerAudioStatus.Ready && string.IsNullOrWhiteSpace(audioObjectKey))
        {
            return Result<Answer>.Failure(AnswerErrors.AudioObjectKeyRequired);
        }

        return Result<Answer>.Success(new Answer(
            id,
            turnId,
            text.Trim(),
            totalCount,
            NormalizeWarnings(warnings),
            string.IsNullOrWhiteSpace(audioObjectKey) ? null : audioObjectKey.Trim(),
            audioStatus,
            audioDurationMs,
            createdAt));
    }

    private static IReadOnlyList<string> NormalizeWarnings(IEnumerable<string>? warnings)
    {
        if (warnings is null)
        {
            return Array.Empty<string>();
        }

        return warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .ToArray();
    }
}
