from datetime import datetime

from app.dates import parse_relative_date, parse_time


NOW = datetime.fromisoformat("2026-10-21T12:00:00+03:00")


def test_today():
    d, _ = parse_relative_date("сегодня", NOW)
    assert d.isoformat() == "2026-10-21"


def test_tomorrow():
    d, _ = parse_relative_date("завтра", NOW)
    assert d.isoformat() == "2026-10-22"


def test_after_tomorrow():
    d, _ = parse_relative_date("послезавтра", NOW)
    assert d.isoformat() == "2026-10-23"


def test_friday():
    d, _ = parse_relative_date("в пятницу", NOW)
    assert d.isoformat() == "2026-10-23"


def test_next_friday():
    d, _ = parse_relative_date("в следующую пятницу", NOW)
    assert d.isoformat() == "2026-10-30"


def test_explicit_date():
    d, _ = parse_relative_date("28 октября", NOW)
    assert d.isoformat() == "2026-10-28"


def test_time_3():
    t, _ = parse_time("в 3")
    assert t == "15:00"


def test_time_11():
    t, _ = parse_time("в 11")
    assert t == "11:00"


def test_time_evening():
    t, _ = parse_time("в 11 вечера")
    assert t == "23:00"


def test_time_noon():
    t, _ = parse_time("в полдень")
    assert t == "12:00"
