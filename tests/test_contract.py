from app.schemas import InterpretRequest, InterpretResponse


def test_search_requires_embedding():
    req = InterpretRequest(
        turn_id="t", text="найди задачу про презентацию",
        now="2026-10-21T12:00:00+03:00", timezone="Europe/Moscow",
    )
    from app.interpret import interpret
    resp = interpret(req)
    assert resp.action.value == "search"
    assert resp.embedding is not None


def test_meeting_without_time_clarifies():
    req = InterpretRequest(
        turn_id="t", text="Поставь встречу с Иваном завтра",
        now="2026-10-21T12:00:00+03:00", timezone="Europe/Moscow",
    )
    from app.interpret import interpret
    resp = interpret(req)
    assert resp.action.value == "clarify"
    assert resp.missing == ["time"]
    assert resp.question


def test_meeting_with_time_creates():
    req = InterpretRequest(
        turn_id="t", text="Добавь встречу с командой завтра в 15:00",
        now="2026-10-21T12:00:00+03:00", timezone="Europe/Moscow",
    )
    from app.interpret import interpret
    resp = interpret(req)
    assert resp.action.value == "create_meeting"
    assert resp.entities.starts_on == "2026-10-22"
    assert resp.entities.starts_at == "15:00"
