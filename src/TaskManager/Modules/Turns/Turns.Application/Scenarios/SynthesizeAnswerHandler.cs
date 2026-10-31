using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Clients.Tts;
using TaskManager.Turns.Application.Configuration;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Синтезирует готовый текст ответа через порт <c>tts</c> и дописывает аудио. Ошибка
/// синтеза не переводит ход в <see cref="TurnState.Failed"/>: меняется только
/// <see cref="AnswerAudioStatus"/> у ответа.
/// </summary>
/// <remarks>
/// Постоянная ошибка (4xx, невалидный ответ) сразу помечает <c>audio_status = failed</c>
/// и не повторяется. Временная ошибка (5xx, таймаут, сеть) пробрасывается: повтор и
/// финальную отметку <c>failed</c> после исчерпания попыток делает планировщик.
/// </remarks>
public sealed class SynthesizeAnswerHandler
{
    private readonly IAnswerRepository _answers;
    private readonly ITtsClient _tts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TtsOptions _options;

    public SynthesizeAnswerHandler(
        IAnswerRepository answers,
        ITtsClient tts,
        IUnitOfWork unitOfWork,
        TtsOptions? options = null)
    {
        _answers = answers;
        _tts = tts;
        _unitOfWork = unitOfWork;
        _options = options ?? TtsOptions.Default;
    }

    public async Task<Result<SynthesizeAnswerResult>> HandleAsync(
        Guid answerId,
        CancellationToken cancellationToken = default)
    {
        if (answerId == Guid.Empty)
        {
            return Result<SynthesizeAnswerResult>.Failure(AnswerErrors.IdRequired);
        }

        Answer? answer = await _answers.GetByIdAsync(answerId, cancellationToken);
        if (answer is null)
        {
            return Result<SynthesizeAnswerResult>.Failure(AnswerErrors.NotFound);
        }

        if (answer.AudioStatus != AnswerAudioStatus.Pending)
        {
            return Result<SynthesizeAnswerResult>.Success(
                new SynthesizeAnswerResult(answer.Id, answer.AudioStatus));
        }

        TtsResult result;
        try
        {
            result = await _tts.SynthesizeAsync(
                new TtsRequest(answer.Id, answer.Text, _options.Voice, _options.Format, _options.Speed),
                cancellationToken);
        }
        catch (TtsException error) when (error.IsPermanent)
        {
            // 4xx — permanent failure: повтор не поможет, пользователь видит текст без озвучки.
            return await MarkFailedAsync(answer, AnswerErrors.AudioSynthesisFailed, cancellationToken);
        }

        // Ключ и длительность должны относиться к запрошенному ответу.
        if (result.AnswerId != answer.Id)
        {
            return await MarkFailedAsync(answer, AnswerErrors.AudioSynthesisFailed, cancellationToken);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            Result ready = answer.MarkAudioReady(result.ObjectKey, result.DurationMs);
            if (ready.IsFailure)
            {
                // Невалидный ответ tts тоже постоянная ошибка: ответ не станет лучше при повторе.
                Result failed = answer.MarkAudioFailed();
                if (failed.IsFailure)
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    return Result<SynthesizeAnswerResult>.Failure(failed.Error!);
                }

                await _unitOfWork.CommitAsync(cancellationToken);
                return Result<SynthesizeAnswerResult>.Failure(ready.Error!);
            }

            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Result<SynthesizeAnswerResult>.Success(
            new SynthesizeAnswerResult(answer.Id, answer.AudioStatus));
    }

    private async Task<Result<SynthesizeAnswerResult>> MarkFailedAsync(
        Answer answer,
        DomainError error,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            Result failed = answer.MarkAudioFailed();
            if (failed.IsFailure)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<SynthesizeAnswerResult>.Failure(failed.Error!);
            }

            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Result<SynthesizeAnswerResult>.Failure(error);
    }
}
