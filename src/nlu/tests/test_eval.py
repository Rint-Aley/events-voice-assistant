import csv
from pathlib import Path

import pytest

from app.schemas import Draft, InterpretRequest
from app.interpret import interpret

CASES = list(csv.DictReader((Path(__file__).parent / "eval_cases.csv").open(encoding="utf-8")))


def _draft_for(case_id: str):
    if case_id == "M6":
        return Draft(kind="meeting", description="встреча с Иваном", starts_on="2026-10-22")
    return None


@pytest.mark.parametrize("case", CASES, ids=[c["id"] for c in CASES])
def test_eval(case):
    req = InterpretRequest(
        turn_id="t",
        text=case["text"],
        now=case["now"],
        timezone="Europe/Moscow",
        draft=_draft_for(case["id"]),
    )
    resp = interpret(req)
    assert resp.action.value == case["expected_action"], f"{case['id']}: action"
    if case["expected_starts_on"]:
        assert resp.entities and resp.entities.starts_on == case["expected_starts_on"], f"{case['id']}: starts_on"
    if case["expected_starts_at"]:
        assert resp.entities and resp.entities.starts_at == case["expected_starts_at"], f"{case['id']}: starts_at"
    if case["expected_missing"]:
        assert resp.missing and case["expected_missing"] in resp.missing, f"{case['id']}: missing"