using System.Globalization;
using TaskManager.Events.Application.Dtos;
using TaskManager.Events.Domain;

namespace TaskManager.Turns.Application.Scenarios;

/// <summary>
/// Тексты ответов из <c>docs/assistant_replies.md</c>. Женский род, обращение на «ты»,
/// без восклицаний и извинений. Даты выводятся словами, время — в формате <c>HH:mm</c>.
/// </summary>
public static class AnswerText
{
    private static readonly string[] Months =
    [
        "января",
        "февраля",
        "марта",
        "апреля",
        "мая",
        "июня",
        "июля",
        "августа",
        "сентября",
        "октября",
        "ноября",
        "декабря",
    ];

    /// <summary>Текст успешного создания записи.</summary>
    public static string Created(EventDto @event, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return @event.Kind switch
        {
            EventKind.Task when @event.StartsOn is { } date =>
                $"Записала задачу: «{@event.Description}» — {FormatDate(date, today)}.",
            EventKind.Task =>
                $"Записала задачу: «{@event.Description}».",
            EventKind.Reminder =>
                $"Напомню: «{@event.Description}» — {FormatMoment(@event, today)}.",
            EventKind.Meeting =>
                $"Записала встречу: «{@event.Description}» — {FormatMoment(@event, today)}.",
            _ => throw new ArgumentOutOfRangeException(nameof(@event), @event.Kind, null),
        };
    }

    /// <summary>Все созданные записи одним текстом, по одной фразе на запись.</summary>
    public static string Created(IReadOnlyList<EventDto> events, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(events);

        return string.Join(" ", events.Select(@event => Created(@event, today)));
    }

    /// <summary>Предупреждение о пересечении встреч; в тексте ответа используется первое.</summary>
    public static string MeetingConflict(string title) =>
        $"На это время уже запланирована встреча «{title}». Выбери другое время.";

    /// <summary>
    /// Заголовок списка: период и общее количество до лимита. Пустой список получает
    /// отдельный текст из <c>assistant_replies.md</c>.
    /// </summary>
    public static string List(
        EventTypeFilter? typeFilter,
        DateOnly from,
        DateOnly to,
        DateOnly today,
        IReadOnlyList<EventDto> events,
        int totalCount)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (totalCount == 0)
        {
            return EmptyList(typeFilter, from, to, today);
        }

        string text = $"{Period(from, to, today)}: {CountLabel(totalCount)}.";

        int hidden = totalCount - events.Count;
        if (hidden > 0)
        {
            text += $" И ещё {CountLabel(hidden)}.";
        }

        return text;
    }

    /// <summary>Склонение слова «запись» по числу.</summary>
    public static string CountLabel(int count)
    {
        int mod100 = count % 100;
        int mod10 = count % 10;

        string word = mod100 is >= 11 and <= 14
            ? "записей"
            : mod10 switch
            {
                1 => "запись",
                2 or 3 or 4 => "записи",
                _ => "записей",
            };

        return $"{count} {word}";
    }

    /// <summary>Дата словами: «сегодня», «завтра», «пт, 23 октября».</summary>
    public static string FormatDate(DateOnly date, DateOnly today)
    {
        if (date == today)
        {
            return "сегодня";
        }

        if (date == today.AddDays(1))
        {
            return "завтра";
        }

        if (date == today.AddDays(-1))
        {
            return "вчера";
        }

        string label = $"{Weekday(date)}, {date.Day} {Months[date.Month - 1]}";

        return date.Year == today.Year ? label : $"{label} {date.Year}";
    }

    public static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string EmptyList(
        EventTypeFilter? typeFilter,
        DateOnly from,
        DateOnly to,
        DateOnly today)
    {
        string period = Period(from, to, today);
        string periodLower = char.ToLowerInvariant(period[0]) + period[1..];

        return typeFilter switch
        {
            EventTypeFilter.Meeting => $"Встреч {periodLower} нет.",
            EventTypeFilter.Task => $"Задач {periodLower} нет.",
            _ => $"{period} ничего нет.",
        };
    }

    private static string FormatMoment(EventDto @event, DateOnly today) =>
        $"{FormatDate(@event.StartsOn!.Value, today)}, {FormatTime(@event.StartsAt!.Value)}";

    private static string Period(DateOnly from, DateOnly to, DateOnly today)
    {
        if (from == to)
        {
            return from == today
                ? "На сегодня"
                : from == today.AddDays(1)
                    ? "На завтра"
                    : $"На {FormatDate(from, today)}";
        }

        DateOnly weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateOnly weekEnd = weekStart.AddDays(6);

        if (from == weekStart && to == weekEnd)
        {
            return "На этой неделе";
        }

        return $"С {FormatDate(from, today)} по {FormatDate(to, today)}";
    }

    private static string Weekday(DateOnly date) =>
        date.DayOfWeek switch
        {
            DayOfWeek.Monday => "пн",
            DayOfWeek.Tuesday => "вт",
            DayOfWeek.Wednesday => "ср",
            DayOfWeek.Thursday => "чт",
            DayOfWeek.Friday => "пт",
            DayOfWeek.Saturday => "сб",
            _ => "вс",
        };
}
