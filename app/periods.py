from __future__ import annotations

import re
from datetime import date, timedelta

from .dates import WEEKDAYS, parse_now


def parse_period(text: str, now_iso: str) -> tuple[date, date] | None:
    t = text.lower()
    now = parse_now(now_iso)
    today = now.date()

    if "послезавтра" in t:
        d = today + timedelta(days=2)
        return d, d
    if "завтра" in t:
        d = today + timedelta(days=1)
        return d, d
    if "сегодня" in t:
        return today, today

    if "на прошлой неделе" in t or "прошлая неделя" in t:
        monday = today - timedelta(days=today.weekday()) - timedelta(days=7)
        return monday, monday + timedelta(days=6)
    if "на следующей неделе" in t or "следующая неделя" in t:
        monday = today - timedelta(days=today.weekday()) + timedelta(days=7)
        return monday, monday + timedelta(days=6)
    if "до конца недели" in t:
        sunday = today - timedelta(days=today.weekday()) + timedelta(days=6)
        return today, sunday
    if "эта неделя" in t or "на этой неделе" in t or "эту неделю" in t:
        monday = today - timedelta(days=today.weekday())
        return monday, monday + timedelta(days=6)
    if "в выходные" in t or "на выходных" in t:
        sat = today - timedelta(days=today.weekday()) + timedelta(days=5)
        return sat, sat + timedelta(days=1)
    if "вчера" in t:
        d = today - timedelta(days=1)
        return d, d

    # конкретный день недели: «в субботу», «в пятницу»
    for name, wd in WEEKDAYS.items():
        if re.search(rf"\bв\s+{name}\b", t):
            delta = (wd - today.weekday()) % 7 or 7
            d = today + timedelta(days=delta)
            return d, d

    return None