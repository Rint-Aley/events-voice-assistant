"""Самопроверка контрактов: схемы парсятся, примеры проходят, негативные кейсы отбиваются.

Запуск:
    pip install jsonschema referencing
    python docs/contracts/validate.py

Скрипт не зависит от приложения и годится для CI: код возврата 1 при любой ошибке.
"""

from __future__ import annotations

import json
import pathlib
import sys

from jsonschema import FormatChecker
from jsonschema.validators import Draft202012Validator
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

try:  # чтобы русский текст не превращался в мохнатые символы в консоли Windows
    sys.stdout.reconfigure(encoding="utf-8")
except (AttributeError, OSError):
    pass

SCHEMA_DIR = pathlib.Path(__file__).resolve().parent

base = {
    "schema_version": 1,
    "message_id": "8c2f1b1e-1111-4a2b-9c3d-000000000001",
    "turn_id": "8c2f1b1e-1111-4a2b-9c3d-000000000002",
    "session_id": "8c2f1b1e-1111-4a2b-9c3d-000000000003",
    "user_id": "8c2f1b1e-1111-4a2b-9c3d-000000000004",
    "attempt": 1,
    "created_at": "2026-10-22T12:00:00+03:00",
}

POSITIVE: dict[str, dict] = {
    "stt.job.json": {
        **base,
        "job_type": "stt",
        "payload": {
            "object_key": "audio/DEMO/6f1c.webm",
            "audio_url": "https://s3.local/bucket/audio/DEMO/6f1c.webm?X-Amz-Signature=abc",
            "content_type": "audio/webm",
            "language": "ru",
            "max_duration_ms": 60000,
        },
    },
    "stt.result.json": {
        **base,
        "job_type": "stt",
        "payload": {
            "text": "напомни завтра в десять позвонить Ивану",
            "language": "ru",
            "duration_ms": 4200,
            "confidence": 0.91,
            "empty": False,
        },
    },
    "nlu.job.json": {
        **base,
        "job_type": "nlu",
        "payload": {
            "text": "напомни завтра в десять позвонить Ивану",
            "reference_datetime": "2026-10-22T18:30:00+03:00",
            "timezone": "Europe/Moscow",
            "pending_draft": None,
            "recent_items": [
                {
                    "id": "8c2f1b1e-1111-4a2b-9c3d-000000000005",
                    "kind": "meeting",
                    "title": "встреча с Иваном",
                    "scheduled_at": "2026-10-20T11:00:00+03:00",
                }
            ],
        },
    },
    "nlu.result.json": {
        **base,
        "job_type": "nlu",
        "payload": {
            "action": "create_task",
            "confidence": 0.93,
            "entities": {
                "title": "позвонить Ивану",
                "datetime_iso": "2026-10-23T10:00:00+03:00",
                "datetime_phrase": "завтра в десять",
                "period": "tomorrow",
                "range": {
                    "from": "2026-10-23T00:00:00+03:00",
                    "to": "2026-10-23T23:59:59+03:00",
                },
                "query": None,
                "target_ref": None,
                "duration_minutes": None,
            },
            "missing_slots": [],
        },
    },
    "tts.job.json": {
        **base,
        "job_type": "tts",
        "payload": {"text": "Создал напоминание", "voice": "default", "format": "mp3"},
    },
    "tts.result.json": {
        **base,
        "job_type": "tts",
        "payload": {
            "object_key": "tts/" + "a" * 64 + ".mp3",
            "duration_ms": 6100,
            "cached": False,
        },
    },
    "dlq.json": {
        "schema_version": 1,
        "message_id": base["message_id"],
        "failed_at": "2026-10-22T12:00:10+03:00",
        "service": "stt",
        "error": {"code": "PROVIDER_TIMEOUT", "message": "timeout after 30s", "retryable": True},
        "original": {**base, "job_type": "stt", "payload": {}},
    },
}


def replace_payload(doc: dict, **changes) -> dict:
    return {**doc, "payload": {**doc["payload"], **changes}}


NEGATIVE: list[tuple[str, dict, str]] = [
    ("stt.result.json", replace_payload(POSITIVE["stt.result.json"], confidence=1.5), "confidence вне диапазона"),
    ("nlu.result.json", replace_payload(POSITIVE["nlu.result.json"], action="delete_everything"), "неизвестный action"),
    ("tts.result.json", replace_payload(POSITIVE["tts.result.json"], object_key="tts/short.mp3"), "сломанный ключ объекта"),
    ("stt.job.json", replace_payload(POSITIVE["stt.job.json"], extra=1), "лишнее поле в payload"),
    ("stt.job.json", {**POSITIVE["stt.job.json"], "schema_version": 2}, "чужой мажор schema_version"),
]


def load_schemas() -> dict[str, dict]:
    return {
        path.name: json.loads(path.read_text(encoding="utf-8"))
        for path in SCHEMA_DIR.glob("*.json")
    }


def main() -> int:
    schemas = load_schemas()
    if not schemas:
        print(f"Не найдено ни одной схемы в {SCHEMA_DIR}")
        return 1

    registry = Registry().with_resources(
        (schema["$id"], Resource.from_contents(schema, default_specification=DRAFT202012))
        for schema in schemas.values()
    )
    validator = Draft202012Validator(
        schemas["envelope.json"], registry=registry, format_checker=FormatChecker()
    )

    def errors(name: str, doc: dict) -> list:
        current = Draft202012Validator(
            schemas[name], registry=registry, format_checker=FormatChecker()
        )
        return sorted(current.iter_errors(doc), key=lambda err: list(err.path))

    ok = True
    print(f"Контракты: {SCHEMA_DIR}")
    print("Позитивные примеры:")
    for name, doc in POSITIVE.items():
        found = errors(name, doc)
        print(f"  {'OK  ' if not found else 'FAIL'} {name}")
        for err in found:
            ok = False
            print(f"        {'/'.join(map(str, err.path))} — {err.message}")

    print("Негативные примеры (ожидается отказ):")
    for name, doc, reason in NEGATIVE:
        found = errors(name, doc)
        print(f"  {'OK  ' if found else 'FAIL'} {name} — {reason}")
        ok = ok and bool(found)

    print()
    print("РЕЗУЛЬТАТ:", "схемы валидны" if ok else "ЕСТЬ ОШИБКИ")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
