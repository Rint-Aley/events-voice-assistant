from __future__ import annotations

import re
from datetime import date, datetime, timedelta

WEEKDAYS = {
    "понедельник": 0, "пн": 0,
    "вторник": 1, "вт": 1,
    "среда": 2, "среду": 2, "ср": 2,
    "четверг": 3, "чт": 3,
    "пятница": 4, "пятницу": 4, "пт": 4,
    "суббота": 5, "субботу": 5, "сб": 5,
    "воскресенье": 6, "вс": 6,
}

MONTHS = {
    "января": 1, "февраля": 2, "марта": 3, "апреля": 4, "мая": 5, "июня": 6,
    "июля": 7, "августа": 8, "сентября": 9, "октября": 10, "ноября": 11, "декабря": 12,
}

NUM_WORDS = {
    "один": 1, "одну": 1, "два": 2, "две": 2, "три": 3, "четыре": 4,
    "пять": 5, "шесть": 6, "семь": 7, "восемь": 8, "девять": 9, "десять": 10,
}


def parse_now(now_iso: str) -> datetime:
    return datetime.fromisoformat(now_iso.replace("Z", "+00:00"))


def next_weekday(today: date, target: int, force_next: bool = False) -> date:
    delta = (target - today.weekday()) % 7
    if delta == 0:
        delta = 7
    return today + timedelta(days=delta)


def parse_relative_date(text: str, now: datetime) -> tuple[date | None, str | None]:
    """Возвращает (дата, фраза) или (None, None).
    Если дата невозможна — (None, '__invalid_date__')."""
    t = text.lower()
    today = now.date()

    if "послезавтра" in t:
        return today + timedelta(days=2), "послезавтра"
    if "завтра" in t:
        return today + timedelta(days=1), "завтра"
    if "сегодня" in t:
        return today, "сегодня"

    # «через два часа», «через 30 минут», «через три дня»
    if "через " in t:
        after = t.split("через ", 1)[1]
        words = after.split()
        if words:
            first = words[0]
            num = None
            if first.isdigit():
                num = int(first)
            elif first in NUM_WORDS:
                num = NUM_WORDS[first]
            if num is not None and len(words) > 1:
                unit = words[1]
                if unit.startswith("час"):
                    dt = now + timedelta(hours=num)
                    return dt.date(), "__also_time__" + dt.strftime("%H:%M")
                if unit.startswith("минут"):
                    dt = now + timedelta(minutes=num)
                    return dt.date(), "__also_time__" + dt.strftime("%H:%M")
                if unit.startswith("дн"):
                    return today + timedelta(days=num), "через " + " ".join(words[:2])

    # «в следующую пятницу» / «на следующей неделе»
    for name, wd in WEEKDAYS.items():
        if re.search(rf"(следующ\w*)\s+{name}\b", t):
            days_to_next_monday = (7 - today.weekday()) % 7 or 7
            next_monday = today + timedelta(days=days_to_next_monday)
            return next_monday + timedelta(days=wd), f"следующая {name}"

    # «в пятницу»
    for name, wd in WEEKDAYS.items():
        if re.search(rf"\bв\s+{name}\b", t):
            return next_weekday(today, wd), f"в {name}"

    # «28 октября»
    m = re.search(r"(\d{1,2})\s+(" + "|".join(MONTHS.keys()) + ")", t)
    if m:
        day, month_name = int(m.group(1)), m.group(2)
        month = MONTHS[month_name]
        year = today.year
        try:
            d = date(year, month, day)
        except ValueError:
            return None, "__invalid_date__"
        if d < today:
            d = date(year + 1, month, day)
        return d, m.group(0)

    return None, None


def parse_time(text: str) -> tuple[str | None, str | None]:
    t = text.lower()

    if "полдень" in t:
        return "12:00", "полдень"

    # «в 15:30», «в 9.30»
    m = re.search(r"\bв\s+(\d{1,2})[:.](\d{2})", t)
    if m:
        h, mi = int(m.group(1)), int(m.group(2))
        if 0 <= h <= 23 and 0 <= mi <= 59:
            return f"{h:02d}:{mi:02d}", m.group(0)

    # «в половине четвёртого»
    if "половине" in t:
        for word, idx in {
            "первого": 1, "второго": 2, "третьего": 3, "четвёртого": 3,
            "четвертого": 3, "пятого": 4, "шестого": 5, "седьмого": 6,
            "восьмого": 7, "девятого": 8, "десятого": 9,
        }.items():
            if word in t:
                return f"{idx:02d}:30", t

    # числительные словами: «в десять», «в десять утра»
    for word, h in NUM_WORDS.items():
        m = re.search(rf"\bв\s+{word}(?:\s*(утра|дня|вечера|ночи))?", t)
        if m:
            period = m.group(1)
            if period == "вечера" and h < 12:
                h += 12
            elif period == "ночи" and h >= 12:
                h -= 12
            elif 1 <= h <= 7 and not period:
                h += 12
            return f"{h:02d}:00", m.group(0)

    # цифрами: «в 15», «в 3 дня», «в 11 вечера»
    m = re.search(r"\bв\s+(\d{1,2})(?:\s*(утра|дня|вечера|ночи))?", t)
    if m:
        h = int(m.group(1))
        period = m.group(2)
        if period == "вечера" and h < 12:
            h += 12
        elif period == "ночи" and h >= 12:
            h -= 12
        elif 1 <= h <= 7 and not period:
            h += 12
        if 0 <= h <= 23:
            return f"{h:02d}:00", m.group(0)

    # «утром / днём / вечером»
    if "утром" in t:
        return "09:00", "утром"
    if "днём" in t or "днем" in t:
        return "13:00", "днём"
    if "вечером" in t:
        return "19:00", "вечером"

    return None, None


def apply_time_without_date(d: date | None, time_str: str | None, now: datetime) -> date | None:
    if d is not None or time_str is None:
        return d
    h, m = map(int, time_str.split(":"))
    candidate = now.replace(hour=h, minute=m, second=0, microsecond=0)
    if candidate <= now:
        return (now + timedelta(days=1)).date()
    return now.date()