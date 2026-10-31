from __future__ import annotations

import re
from datetime import date, timedelta
from .dates import apply_time_without_date, parse_now, parse_relative_date, parse_time
from .periods import parse_period

MEETING_HINTS = ("встреч", "созвон", "созвониться", "переговоры")
REMINDER_HINTS = ("напомни", "напомнить", "напоминание")


def detect_action(text: str) -> str:
    t = text.lower()
    if any(h in t for h in REMINDER_HINTS):
        return "create_reminder"
    # list_period раньше create_meeting — «есть ли у меня встречи» это список
    if any(w in t for w in (
        "что у меня", "какие планы", "покажи", "что запланировано",
        "какие задачи", "какие встречи", "есть ли у меня", "что на",
    )):
        return "list_period"
    # search раньше create_meeting — «когда была встреча» это поиск
    if any(w in t for w in (
        "найди", "поищи", "когда была", "когда был",
        "когда у меня была", "когда у меня был",
    )):
        return "search"
    if any(h in t for h in MEETING_HINTS):
        return "create_meeting"
    if any(w in t for w in (
        "поставь задачу", "надо бы", "нужно", "купить", "отправить",
        "позвонить", "забрать", "сделать", "разобрать", "выключить",
    )):
        return "create_task"
    return "unknown"

def extract_description(text: str) -> str | None:
    t = text.strip()
    t = re.sub(
        r"^(напомни(\s+мне)?|напомнить|поставь задачу|поставь|"
        r"добавь встречу|добавь|запиши встречу|запиши|созвон|встреча)\s+",
        "", t, flags=re.I,
    )
    t = re.sub(r"\b(сегодня|завтра|послезавтра|вчера)\b", "", t, flags=re.I)
    t = re.sub(r"\bв\s+(понедельник|вторник|среду|четверг|пятницу|субботу|воскресенье)\b", "", t, flags=re.I)
    t = re.sub(r"\bв\s+\d{1,2}([:.]\d{2})?(\s+(утра|дня|вечера|ночи))?\b", "", t, flags=re.I)
    t = re.sub(r"\bчерез\s+\S+(\s+\S+)?", "", t, flags=re.I)
    t = re.sub(
        r"\b\d{1,2}\s+(января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)\b",
        "", t, flags=re.I,
    )
    t = re.sub(r"\bдо конца недели\b", "", t, flags=re.I)
    t = re.sub(r"\bна следующей неделе\b", "", t, flags=re.I)
    t = re.sub(r"\bна этой неделе\b", "", t, flags=re.I)
    t = re.sub(r"\bв выходные\b", "", t, flags=re.I)
    t = re.sub(r"\b(мне|меня|я|у меня)\b", "", t, flags=re.I)
    t = re.sub(r"\s+", " ", t).strip(" ,.;-")
    return t or None


def interpret_rules(text: str, now_iso: str, draft: dict | None) -> dict:
    action = detect_action(text)
    now = parse_now(now_iso)
    merged = dict(draft or {})

    d, date_phrase = parse_relative_date(text, now)
    if date_phrase == "__invalid_date__":
        return {"action": "clarify", "confidence": 0.9,
                "missing": ["datetime"], "question": "Такой даты нет. На какой день поставить?"}

    # «через два часа» — дата и время вместе
    embedded_time = None
    if date_phrase and date_phrase.startswith("__also_time__"):
        embedded_time = date_phrase.replace("__also_time__", "")
        date_phrase = None

    t, _ = parse_time(text)
    if embedded_time:
        t = embedded_time

    # если дата не найдена в тексте, но есть в draft — берём из draft
    if d is None and merged.get("starts_on"):
        try:
            d = date.fromisoformat(merged["starts_on"])
        except ValueError:
            d = None

    d = apply_time_without_date(d, t, now)

    # дедлайны
    is_deadline = False
    t_lower = text.lower()
    if "до конца недели" in t_lower or "на этой неделе" in t_lower:
        sunday = now.date() - timedelta(days=now.weekday()) + timedelta(days=6)
        d = sunday
        is_deadline = True

    entities: dict = {}
    if action == "unknown" and merged.get("kind") == "meeting":
        action = "create_meeting"
    if action in ("create_task", "create_reminder", "create_meeting"):
        desc = merged.get("description") or extract_description(text)
        if desc:
            entities["description"] = desc
        if d:
            entities["starts_on"] = d.isoformat()
        elif merged.get("starts_on"):
            entities["starts_on"] = merged["starts_on"]
        if t:
            entities["starts_at"] = t
        elif merged.get("starts_at"):
            entities["starts_at"] = merged["starts_at"]
        if action == "create_meeting":
            entities["duration_minutes"] = 60
        if is_deadline:
            entities["is_deadline"] = True

    if action == "list_period":
        period = parse_period(text, now_iso)
        if not period:
            return {"action": "clarify", "confidence": 0.6,
                    "missing": ["datetime"], "question": "За какой период показать?"}
        entities["period_from"], entities["period_to"] = period[0].isoformat(), period[1].isoformat()

    if action == "search":
        q = text.strip()
        q = re.sub(r"^(найди|поищи|когда была|когда был|когда у меня была|когда у меня был)\s+", "", q, flags=re.I)
        q = re.sub(r"^(задачу|встречу|запись)\s+", "", q, flags=re.I)
        q = q.strip(" ?.,")
        if not q:
            return {"action": "clarify", "confidence": 0.6,
                    "missing": ["query"], "question": "Что найти?"}
        entities["query"] = q

    if action == "create_meeting":
        has_on = bool(entities.get("starts_on"))
        has_at = bool(entities.get("starts_at"))
        if not has_on and not has_at:
            return {"action": "clarify", "confidence": 0.9,
                    "missing": ["datetime"], "question": "На какой день и время?"}
        if not has_at:
            return {"action": "clarify", "confidence": 0.9,
                    "missing": ["time"], "question": "На какое время поставить встречу?"}

    if action == "create_task" and not entities.get("description"):
        return {"action": "clarify", "confidence": 0.7,
                "missing": ["description"], "question": "О чём напомнить?"}

    if action == "unknown":
        return {"action": "unknown", "confidence": 0.2}

    return {"action": action, "confidence": 0.75, "entities": entities or None}