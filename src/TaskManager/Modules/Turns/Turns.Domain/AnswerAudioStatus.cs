namespace TaskManager.Turns.Domain;

/// <summary>Состояние озвучки ответа.</summary>
public enum AnswerAudioStatus
{
    /// <summary>Задача <c>tts</c> поставлена.</summary>
    Pending,

    /// <summary>Ключ аудио и длительность записаны.</summary>
    Ready,

    /// <summary>Озвучка не удалась; пользователь видит текст.</summary>
    Failed,

    /// <summary>Озвучка не требовалась.</summary>
    Skipped,
}
