using TaskManager.Application.Abstractions;
using TaskManager.SharedKernel;
using TaskManager.Turns.Application.Clients.Stt;
using TaskManager.Turns.Application.Configuration;
using TaskManager.Turns.Application.Ports;
using TaskManager.Turns.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Обработчик <c>stt</c>: читает ход в <see cref="TurnState.Transcribing"/>, создаёт
/// presigned GET и передаёт ссылку в <c>stt</c>. Результат применяется сценарием
/// <see cref="HandleSttResultHandler"/>; поздний ответ после смены состояния отбрасывается.
/// </summary>
public sealed class TranscribeTurnHandler
{
    private readonly ISttClient _stt;
    private readonly IObjectStorage _storage;
    private readonly ITurnRepository _turns;
    private readonly HandleSttResultHandler _result;
    private readonly SttTurnOptions _options;

    public TranscribeTurnHandler(
        ISttClient stt,
        IObjectStorage storage,
        ITurnRepository turns,
        HandleSttResultHandler result,
        SttTurnOptions? options = null)
    {
        _stt = stt;
        _storage = storage;
        _turns = turns;
        _result = result;
        _options = options ?? SttTurnOptions.Default;
    }

    public async Task<Result<SubmitTurnResult>> HandleAsync(
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        if (turnId == Guid.Empty)
        {
            return Result<SubmitTurnResult>.Failure(TurnErrors.IdRequired);
        }

        Turn? turn = await _turns.GetByIdAsync(turnId, cancellationToken);
        if (turn is null)
        {
            return Result<SubmitTurnResult>.Failure(TurnErrors.TurnNotFound);
        }

        // Поздняя задача после EXPIRED/FAILED не должна заново дёргать stt.
        if (turn.State != TurnState.Transcribing)
        {
            return Result<SubmitTurnResult>.Success(new SubmitTurnResult(turn.Id, turn.State));
        }

        if (string.IsNullOrWhiteSpace(turn.AudioObjectKey))
        {
            return Result<SubmitTurnResult>.Failure(TurnErrors.AudioObjectKeyRequired);
        }

        Uri audioUrl = await _storage.CreatePresignedDownloadUrlAsync(
            turn.AudioObjectKey,
            cancellationToken);

        // Контракт stt требует тот же content_type, с которым клиент загрузил файл.
        // Тип хранится в расширении ключа, отдельной колонки у хода нет.
        string contentType = _storage.GetContentType(turn.AudioObjectKey);

        SttResult result = await _stt.TranscribeAsync(
            new SttRequest(
                turn.Id,
                audioUrl,
                contentType,
                _options.Language,
                _options.MaxDurationMs),
            cancellationToken);

        // Ответ обязан относиться к запрошенному ходу; иначе это нарушение контракта,
        // а не временная ошибка, и повтор не поможет.
        if (result.TurnId != turn.Id)
        {
            throw new SttException(
                "stt returned a result for another turn.",
                isPermanent: true);
        }

        return await _result.HandleAsync(result, cancellationToken);
    }
}
