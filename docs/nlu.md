# NLU (Natural Language Understanding)

Сервис разбора текста в план действия. Принимает текст запроса и контекст хода,
возвращает действие, сущности и эмбеддинг. Основано на `architecture.md` §4.4, §5.2
и контрактах `contracts/nlu.request.json`, `contracts/nlu.response.json`.

## 1. Ответственность и границы

- **Делает:** определяет `action`, извлекает и нормализует сущности (описание, даты,
  время, период, поисковый запрос), считает эмбеддинг текста, возвращает недостающие
  слоты и уточняющий вопрос.
- **Не делает:** не выполняет действия, не пишет в БД, не вызывает `stt`/`tts`,
  не форматирует ответ пользователю и не перепроверяет бизнес-правила дат
  (существует ли дата, не в прошлом ли она) — это зона TaskManager.

## 2. Вход и выход (контракты)

- **Вход:** `POST /v1/interpret` (`nlu.request.json`).
  - `turn_id` (UUID), `text` (1–1000), `now` (date-time с offset),
    `timezone` (IANA), `draft` (известные слоты предыдущего хода или `null`).
- **Выход:** `nlu.response.json`.
  - `action`: `create_task`, `create_reminder`, `create_meeting`, `list_period`,
    `search`, `clarify`, `unknown`.
  - `confidence` (0..1), `entities`, `missing`, `question`, `embedding`,
    `provider`, `model`.
- **Инварианты:** `search` требует `entities.query` и `embedding`; `clarify` —
  `missing` и `question`; `create_meeting` — `starts_on` + `starts_at`;
  `description` и `embedding` возвращаются парой.
- **Ошибки:** 400 — невалидная схема, 422 — `interpret` не смог построить план
  (`ValueError`), 500 — внутренняя ошибка.

## 3. Внутреннее устройство

- **Rules-based ядро:** разбор относительных дат и времени (`завтра`, `в пятницу`,
  `через два часа`, `28 октября`, `в 15:30`, `в 3` → 15:00), периодов и интентов.
  Работает без внешних вызовов.
- **LLM:** OpenAI-совместимый `chat/completions` с `response_format=json_object`,
  `temperature=0`. При ошибке или невалидном JSON используется rules-результат
  (`provider=rules_fallback`).
- **`RULES_ONLY=true`:** LLM не вызывается вообще (`provider=rules`). Используется
  в тестах, офлайн-демо и при отсутствии ключей.
- **Эмбеддинги:** `text-embedding-3-small`, размерность **1536**. Для
  `create_*`/`clarify` эмбеддится `description`, для `search` — `query`.
  Без ключа или в `RULES_ONLY` возвращается детерминированный stub `sha256`
  той же размерности (`model=stub-sha256`).
- **Мерж черновика:** известные слоты из `draft` сохраняются, новые значения из
  текста их дополняют; невалидные даты не попадают в слоты.

## 4. Конфигурация (env)

| Переменная | Значение по умолчанию |
|---|---|
| `LLM_BASE_URL` | `https://api.openai.com/v1` |
| `LLM_API_KEY` | пусто (без ключа используется rules-only) |
| `LLM_MODEL` | `gpt-4o-mini` |
| `LLM_TIMEOUT` | `20` |
| `EMBEDDING_BASE_URL` | `LLM_BASE_URL` |
| `EMBEDDING_API_KEY` | `LLM_API_KEY` |
| `EMBEDDING_MODEL` | `text-embedding-3-small` |
| `EMBEDDING_DIM` | `1536` |
| `RULES_ONLY` | `false` |
| `TIMEZONE` | `Europe/Moscow` |

`EMBEDDING_MODEL`/`EMBEDDING_DIM` зафиксированы и совпадают с ожиданиями TaskManager.

### Внешняя интеграция

Сервису не нужны Postgres, S3 или другие контейнеры: он либо работает локально
(rules + stub-эмбеддинг), либо обращается к OpenAI-совместимому HTTP API.

- Без `LLM_API_KEY` сервис автоматически работает как rules-only, даже при
  `RULES_ONLY=false`.
- Для полноценного разбора задайте `LLM_BASE_URL`, `LLM_API_KEY`, `LLM_MODEL`.
- Для реальных эмбеддингов — `EMBEDDING_BASE_URL`/`EMBEDDING_API_KEY` (по умолчанию
  берутся из LLM-настроек) и модель с размерностью 1536.
- Подойдёт OpenAI, Azure OpenAI с совместимым endpoint, Ollama/LM Studio/vLLM.
  Для локального LLM на хосте: `LLM_BASE_URL=http://host.docker.internal:11434/v1`
  (в compose этот адрес уже проброшен через `extra_hosts`).

## 5. Таймауты и лимиты

- `LLM_TIMEOUT` — таймаут HTTP-вызова LLM и эмбеддингов, по умолчанию 20 с.
- `text` — 1000 символов; `description`/`query` — 500; `question` — 300.
- `confidence` ограничен диапазоном 0..1.
- Сервис stateless; идемпотентность определяется `turn_id` и детерминированностью
  правил/эмбеддинга.

## 6. Сборка и запуск

- **Исходники:** `src/nlu/`.
- **Порт:** `8001` (TaskManager по умолчанию ходит на `http://localhost:8001`).
- **Docker:** `docker build -t todo-nlu src/nlu`.
- **Compose:** сервис `voice-nlu` из `deploy/docker-compose.yml`, порт `NLU_PORT`.
- **Локально:**
  ```bash
  cd src/nlu
  pip install -r requirements.txt
  uvicorn app.main:app --reload --port 8001
  ```
- **Тесты:** `RULES_ONLY=true pytest -q`.

Проверка:

```bash
curl -s localhost:8001/v1/interpret -H 'Content-Type: application/json' \
  -d '{"turn_id":"8c2f1b1e-1111-4a2b-9c3d-000000000001","text":"Добавь встречу с командой завтра в 15:00","now":"2026-10-21T12:00:00+03:00","timezone":"Europe/Moscow","draft":null}'
```

Ожидается `action=create_meeting`, `starts_on=2026-10-22`, `starts_at=15:00`,
`embedding` с размерностью 1536.

## 7. Отложено

- Гибридный поиск и порог отсечения — зона TaskManager.
- Смена embedding-модели — эксплуатационная задача с переиндексацией.
- Мультиязычность: сейчас `language=ru` и русские правила.
