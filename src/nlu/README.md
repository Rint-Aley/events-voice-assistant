# NLU service

Текст → план действия для TaskManager. Контракты: `../../docs/contracts/nlu.request.json`,
`../../docs/contracts/nlu.response.json`.

## Запуск

```bash
cd src/nlu
cp .env.example .env
# Заполните LLM_BASE_URL, LLM_API_KEY, LLM_MODEL и параметры EMBEDDING_*
pip install -r requirements.txt
uvicorn app.main:app --reload --port 8001
```

Без ключей (rules-only, для тестов и демо):

```bash
RULES_ONLY=true uvicorn app.main:app --reload --port 8001
```

## Docker

```bash
docker build -t todo-nlu src/nlu
docker run --rm -p 8001:8001 -e RULES_ONLY=true todo-nlu
```

В общем compose это сервис `voice-nlu`; порт публикуется как `NLU_PORT` (по умолчанию
`8001`). TaskManager по умолчанию ходит на `http://localhost:8001`.

## Проверка

```bash
curl -s localhost:8001/v1/interpret -H 'Content-Type: application/json' \
  -d @examples/req.json
curl -s localhost:8001/health
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
- Эмбеддинги: `EMBEDDING_MODEL=text-embedding-3-small`, `EMBEDDING_DIM=1536`.
  Размерность должна совпадать с `EMBEDDING_DIM` в TaskManager.

## Ограничения

- Не валидирует даты на уровне бизнес-правил.
- Не пишет в БД.
- Не форматирует ответ для пользователя.
- Для реальных запросов нужны действующие ключи и совместимый API; без ключей
  используется rules-based fallback и детерминированный stub-эмбеддинг.
