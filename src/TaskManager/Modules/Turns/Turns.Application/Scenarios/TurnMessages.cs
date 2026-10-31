namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Авторитетные тексты ответов приёма запроса. Формулировки совпадают с
/// <c>docs/task-manager/implementation.md</c> §10.3–10.4 и HTTP-контрактом отмены.
/// </summary>
public static class TurnMessages
{
    public const string DraftCancelled = "Хорошо, не сохраняю";

    public const string ClarificationExhausted =
        "Не удалось уточнить. Сформулируй запрос иначе.";

    public const string DescriptionQuestion = "Что записать?";

    public const string InvalidDateQuestion =
        "Такой даты не существует. На какой день поставить?";

    public const string InvalidTimeQuestion =
        "Такого времени не существует. На какое время поставить?";

    public const string PastTimeQuestion =
        "Это время уже прошло. На какое время перенести?";

    public static string DateQuestion(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? "На какой день поставить?"
            : $"На какой день поставить «{title}»?";

    public static string TimeQuestion(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? "На какое время поставить?"
            : $"На какое время поставить «{title}»?";

    public static string DateTimeQuestion(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? "На когда поставить?"
            : $"На когда поставить «{title}»?";
}
