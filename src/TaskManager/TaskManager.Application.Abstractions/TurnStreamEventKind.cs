namespace TaskManager.Application.Abstractions;

/// <summary>Виды событий потока хода, из которых Api строит SSE-фреймы.</summary>
public enum TurnStreamEventKind
{
    /// <summary>Переход состояния хода.</summary>
    State,

    /// <summary>Ответ готов, ждать TTS не нужно.</summary>
    Answer,

    /// <summary>Аудио ответа готово.</summary>
    Audio,

    /// <summary>Обработка хода завершилась ошибкой.</summary>
    Error,
}
