using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Помечает озвучку ответа неудачной. Ошибка <c>tts</c> не переводит ход в
/// <see cref="TurnState.Failed"/>: пользователь видит текст без аудио. Повторная отметка
/// идемпотентна.
/// </summary>
public sealed class MarkAnswerAudioFailedHandler
{
    private readonly IAnswerRepository _answers;
    private readonly IUnitOfWork _unitOfWork;

    public MarkAnswerAudioFailedHandler(
        IAnswerRepository answers,
        IUnitOfWork unitOfWork)
    {
        _answers = answers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AnswerCloseResult>> HandleAsync(
        Guid answerId,
        CancellationToken cancellationToken = default)
    {
        if (answerId == Guid.Empty)
        {
            return Result<AnswerCloseResult>.Failure(AnswerErrors.IdRequired);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            // Блокировка строки исключает гонку с успешным синтезом: поздний провал
            // читает актуальный audio_status и не затирает готовое аудио.
            Answer? answer = await _answers.GetByIdForUpdateAsync(answerId, cancellationToken);
            if (answer is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<AnswerCloseResult>.Failure(AnswerErrors.NotFound);
            }

            // Готовое аудио поздним провалом не отменяется.
            if (answer.AudioStatus == AnswerAudioStatus.Ready)
            {
                AnswerAudioStatus status = answer.AudioStatus;
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<AnswerCloseResult>.Success(
                    new AnswerCloseResult(status, Changed: false));
            }

            bool changed = answer.AudioStatus == AnswerAudioStatus.Pending;

            Result failed = answer.MarkAudioFailed();
            if (failed.IsFailure)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return Result<AnswerCloseResult>.Failure(failed.Error!);
            }

            await _unitOfWork.CommitAsync(cancellationToken);

            return Result<AnswerCloseResult>.Success(
                new AnswerCloseResult(answer.AudioStatus, changed));
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
