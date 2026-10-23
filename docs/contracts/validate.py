"""Самопроверка HTTP-контрактов: схемы парсятся, примеры проходят, негативные кейсы отбиваются.

Запуск:
    pip install jsonschema referencing
    python docs/contracts/validate.py

Скрипт не зависит от приложения и годится для CI: код возврата 1 при любой ошибке.
Архивные схемы брокера проверяются отдельно: docs/contracts/archive/validate.py.
"""

from __future__ import annotations

import json
import pathlib
import sys

from jsonschema import FormatChecker
from jsonschema.validators import Draft202012Validator

try:  # чтобы русский текст не превращался в мохнатые символы в консоли Windows
    sys.stdout.reconfigure(encoding="utf-8")
except (AttributeError, OSError):
    pass

SCHEMA_DIR = pathlib.Path(__file__).resolve().parent

TURN = "8c2f1b1e-1111-4a2b-9c3d-000000000001"
ANSWER = "8c2f1b1e-1111-4a2b-9c3d-000000000002"
HASH64 = "0123456789abcdef" * 4

EMBEDDING = {"model": "bge-m3", "dim": 4, "values": [0.1, -0.2, 0.3, 0.4]}

POSITIVE: list[tuple[str, dict, str]] = [
    (
        "stt.request.json",
        {
            "turn_id": TURN,
            "audio_url": "https://s3.local/audio/in/abc.webm?X-Amz-Signature=deadbeef",
            "content_type": "audio/webm",
            "language": "ru",
            "max_duration_ms": 60000,
        },
        "голосовой запрос",
    ),
    (
        "stt.response.json",
        {
            "turn_id": TURN,
            "text": "напомни завтра в 10 позвонить Ивану",
            "language": "ru",
            "confidence": 0.94,
            "empty": False,
            "duration_ms": 4200,
            "provider": "whisper",
            "model": "large-v3",
        },
        "речь распознана",
    ),
    (
        "stt.response.json",
        {
            "turn_id": TURN,
            "text": "",
            "language": "ru",
            "confidence": 0.0,
            "empty": True,
            "duration_ms": 3000,
            "provider": "whisper",
        },
        "тишина в записи",
    ),
    (
        "nlu.request.json",
        {
            "turn_id": TURN,
            "text": "что у меня на неделе",
            "now": "2026-10-24T12:00:00+03:00",
            "timezone": "Europe/Moscow",
            "draft": None,
        },
        "без черновика",
    ),
    (
        "nlu.request.json",
        {
            "turn_id": TURN,
            "text": "в пятницу в 15",
            "now": "2026-10-24T12:00:00+03:00",
            "timezone": "Europe/Moscow",
            "draft": {
                "kind": "meeting",
                "description": "обсудить релиз",
                "starts_on": "2026-10-30",
            },
        },
        "с черновиком",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "create_reminder",
            "confidence": 0.96,
            "entities": {
                "description": "позвонить Ивану",
                "starts_on": "2026-10-25",
                "starts_at": "10:00",
            },
            "embedding": EMBEDDING,
        },
        "напоминание с описанием и вектором",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "create_task",
            "confidence": 0.81,
            "entities": {"starts_on": "2026-10-25"},
            "embedding": None,
        },
        "задача без описания: вектора нет",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "list_period",
            "confidence": 0.9,
            "entities": {"period_from": "2026-10-19", "period_to": "2026-10-25"},
            "embedding": {"model": "bge-m3", "dim": 3, "values": [0.0, 0.1, 0.2]},
        },
        "выборка за период",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "search",
            "confidence": 0.88,
            "entities": {"query": "встреча с Иваном"},
            "embedding": {"model": "bge-m3", "dim": 3, "values": [0.5, 0.1, -0.4]},
        },
        "поиск по смыслу",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "clarify",
            "confidence": 0.6,
            "missing": ["description"],
            "question": "Что нужно сделать?",
            "embedding": None,
        },
        "уточнение",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "unknown",
            "confidence": 0.2,
            "embedding": None,
        },
        "не понял",
    ),
    (
        "tts.request.json",
        {
            "answer_id": ANSWER,
            "text": "Создал напоминание на 25 октября, 10:00.",
            "voice": "ru-female-1",
            "format": "ogg_opus",
            "speed": 1.0,
        },
        "синтез ответа",
    ),
    (
        "tts.response.json",
        {
            "answer_id": ANSWER,
            "object_key": f"tts/{HASH64}.opus",
            "content_type": "audio/ogg",
            "format": "ogg_opus",
            "duration_ms": 5200,
            "cached": False,
        },
        "аудио готово",
    ),
    (
        "error.json",
        {
            "error": {
                "code": "PROVIDER_UNAVAILABLE",
                "message": "STT provider did not respond in time",
                "retryable": True,
                "details": {"provider": "whisper"},
            }
        },
        "ошибка сервиса",
    ),
]

NEGATIVE: list[tuple[str, dict, str]] = [
    (
        "stt.request.json",
        {
            "turn_id": TURN,
            "audio_url": "https://s3.local/audio/in/abc.webm",
            "content_type": "audio/webm",
            "language": "ru",
            "model": "large-v3",
        },
        "лишнее поле в запросе",
    ),
    (
        "stt.request.json",
        {
            "turn_id": TURN,
            "audio_url": "https://s3.local/audio/in/abc.flac",
            "content_type": "audio/flac",
            "language": "ru",
        },
        "неподдерживаемый формат",
    ),
    (
        "stt.response.json",
        {
            "turn_id": TURN,
            "text": "привет",
            "language": "ru",
            "confidence": 0.9,
            "empty": True,
            "duration_ms": 1000,
            "provider": "whisper",
        },
        "empty = true и непустой текст",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "delete_task",
            "confidence": 0.9,
            "embedding": None,
        },
        "неизвестный action",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "create_task",
            "confidence": 0.9,
            "embedding": None,
        },
        "создание без entities",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "create_meeting",
            "confidence": 0.9,
            "entities": {"description": "обсудить релиз", "starts_on": "2026-10-30"},
            "embedding": None,
        },
        "описание без вектора",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "search",
            "confidence": 0.9,
            "entities": {"query": "встреча с Иваном"},
            "embedding": None,
        },
        "поиск без вектора",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "clarify",
            "confidence": 0.9,
            "missing": ["time"],
            "embedding": None,
        },
        "уточнение без вопроса",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "unknown",
            "confidence": 0.2,
            "entities": {"description": "что-то"},
            "embedding": None,
        },
        "«не понял» с сущностями",
    ),
    (
        "nlu.response.json",
        {
            "turn_id": TURN,
            "action": "create_reminder",
            "confidence": 0.9,
            "entities": {"description": "позвонить", "starts_at": "10:00"},
            "embedding": EMBEDDING,
        },
        "время без даты",
    ),
    (
        "tts.response.json",
        {
            "answer_id": ANSWER,
            "object_key": f"tts/{HASH64}.opus",
            "content_type": "audio/ogg",
            "format": "mp3",
            "duration_ms": 5200,
            "cached": False,
        },
        "формат не совпадает с расширением",
    ),
    (
        "error.json",
        {"error": {"code": "BAD_REQUEST", "message": "text is required"}},
        "ошибка без retryable",
    ),
]


def load_schema(name: str) -> dict:
    path = SCHEMA_DIR / name
    with path.open(encoding="utf-8") as fh:
        return json.load(fh)


def check(
    schema: dict,
    doc: dict,
) -> list[str]:
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    errors = sorted(validator.iter_errors(doc), key=lambda e: list(e.path))
    return [e.message for e in errors]


def run(cases: list[tuple[str, dict, str]], expect_valid: bool) -> bool:
    ok = True
    schemas: dict[str, dict] = {}
    for name, doc, label in cases:
        if name not in schemas:
            try:
                schemas[name] = load_schema(name)
            except (OSError, json.JSONDecodeError) as exc:
                print(f"  FAIL {name}: схема не читается — {exc}")
                ok = False
                continue

        errors = check(schemas[name], doc)
        passed = (not errors) == expect_valid
        mark = "OK  " if passed else "FAIL"
        print(f"  {mark} {name} — {label}")
        if not passed:
            ok = False
            for message in errors[:3]:
                print(f"       {message}")
    return ok


def main() -> int:
    print(f"Контракты: {SCHEMA_DIR}")
    print("Позитивные примеры:")
    ok = run(POSITIVE, expect_valid=True)

    print("Негативные примеры (ожидается отказ):")
    ok = run(NEGATIVE, expect_valid=False) and ok

    print()
    if ok:
        print("РЕЗУЛЬТАТ: контракты согласованы")
        return 0
    print("РЕЗУЛЬТАТ: есть ошибки")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
