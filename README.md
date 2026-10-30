# NLU service

Текст → план действия. Контракт: `docs/contracts/nlu.request.json`, `docs/contracts/nlu.response.json` (эти спецификации должны быть предоставлены отдельно, если они нужны проекту).

## Запуск

```bash
cp .env.example .env
# Заполните LLM_BASE_URL, LLM_API_KEY, LLM_MODEL и параметры EMBEDDING_*
pip install -r requirements.txt
uvicorn app.main:app --reload --port 8001
```

Без ключей (rules-only, для тестов и демо):

```bash
RULES_ONLY=true uvicorn app.main:app --reload --port 8001
```

## Проверка

```bash
curl -s localhost:8001/v1/interpret -H 'Content-Type: application/json' -d '{
  "turn_id": "8c2f1b1e-1111-4a2b-9c3d-000000000001",
  "text": "Добавь встречу с командой завтра в 15:00",
  "now": "2026-10-21T12:00:00+03:00",
  "timezone": "Europe/Moscow",
  "draft": null
}'
```

## Тесты

```bash
RULES_ONLY=true pytest -q
```

- `test_dates.py` — правила относительных дат.
- `test_contract.py` — инварианты контракта.
- `test_eval.py` — набор примеров из `eval_cases.csv`.

## Настройки моделей

- LLM: `LLM_MODEL` через OpenAI-совместимый API.
- Эмбеддинги: `EMBEDDING_MODEL`, `EMBEDDING_DIM`. Размерность должна совпадать с размерностью вектора, ожидаемой backend.

## Ограничения

- Не валидирует даты на уровне бизнес-правил.
- Не пишет в БД.
- Не форматирует ответ для пользователя.
- Для реальных запросов нужны действующие ключи и совместимый API.
