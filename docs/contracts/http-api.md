# HTTP + SSE контракт: web ↔ TaskManager

Версия: 1. Базовый префикс: `/api/v1`. Формат — JSON, `Content-Type: application/json`
(кроме SSE). Аутентификации нет: пользователь один, `user_id = DEMO_USER_ID`.

Часть набора HTTP-контрактов: обзор и правила — в `README.md`, схемы обмена с сервисами —
там же.

## 1. Загрузка аудио

`POST /api/v1/uploads`

```json
{ "content_type": "audio/webm", "size_bytes": 42000 }
```

Ответ `201`:

```json
{
  "object_key": "audio/DEMO/6f1c...c2.webm",
  "upload_url": "https://s3.../presigned-put",
  "expires_at": "2026-10-22T12:10:00Z"
}
```

Клиент делает `PUT` файла по `upload_url` (без заголовка `Authorization`), затем передаёт
`object_key` в `POST /messages`.

Ограничения: `content_type` ∈ {`audio/webm`, `audio/ogg`, `audio/wav`, `audio/mpeg`},
`size_bytes` ≤ 10 МБ. TTL ссылки — 10 минут.

## 2. Отправка запроса

`POST /api/v1/messages`

```json
{
  "session_id": "uuid",
  "idempotency_key": "uuid",
  "text": "напомни завтра в 10 позвонить Ивану",
  "object_key": null
}
```

Ровно одно из `text` / `object_key` заполнено, второе — `null`.

Ответ `202`:

```json
{ "turn_id": "uuid", "state": "TRANSCRIBING", "events_url": "/api/v1/turns/<turn_id>/events" }
```

`state` — `TRANSCRIBING` для голосового запроса и `INTERPRETING` для текстового.

- `idempotency_key` генерирует клиент (uuid на отправку). Повтор с тем же ключом возвращает
  тот же `turn_id` и `202`, новое действие не создаётся.
- `text` ≤ 1000 символов.

## 3. Состояние хода

`GET /api/v1/turns/{turn_id}` — polling-fallback для SSE.

```json
{
  "turn_id": "uuid",
  "state": "COMPLETED",
  "text_input": "напомни завтра в 10 позвонить Ивану",
  "answer": "Создал напоминание «позвонить Ивану» на 23 октября, 10:00.",
  "events": [
    { "id": "uuid", "kind": "reminder", "description": "позвонить Ивану", "starts_on": "2026-10-23", "starts_at": "10:00:00+03" }
  ],
  "audio": { "url": "https://s3.../presigned-get", "duration_ms": 6100 },
  "error": null
}
```

Коды состояний — из `docs/task-manager.md` §4. `events` — результат действия (созданные или
найденные записи), может быть пустым массивом.

## 4. Поток событий

`GET /api/v1/turns/{turn_id}/events` — `text/event-stream`.

| event | data |
|---|---|
| `state` | `{"state":"TRANSCRIBING"}` |
| `answer` | `{"text":"...","events":[...]}` |
| `audio` | `{"url":"https://...","duration_ms":6100}` |
| `error` | `{"code":"STT_FAILED","message":"..."}` |

Правила:

- `state` приходит на каждый переход; терминальные состояния — `COMPLETED`, `FAILED`,
  `EXPIRED`.
- `answer` и `error` взаимоисключающие, приходят один раз.
- `audio` приходит **после** `answer` (озвучка догоняет) и может не прийти вовсе — это не
  ошибка.
- Переподключение: журнал событий не хранится. При подключении сервер отдаёт снапшот —
  текущий `state`, затем `answer` (если есть) и `audio` (если готово), после чего продолжает
  живой поток. Клиент отбрасывает дубли по типу события.
- Keep-alive: комментарий `: ping` каждые 15 с.

## 5. Ошибки

Единый формат:

```json
{ "error": { "code": "TURN_NOT_FOUND", "message": "Ход не найден", "retryable": false } }
```

| HTTP | code | Когда |
|---|---|---|
| 400 | `BAD_REQUEST` | оба или ни одного из `text`/`object_key`; пустой текст |
| 400 | `UNSUPPORTED_AUDIO` | недопустимый `content_type` |
| 404 | `TURN_NOT_FOUND` | неизвестный `turn_id` |
| 409 | `SESSION_BUSY` | предыдущий ход ещё не завершён (в MVP сессия одна) |
| 413 | `PAYLOAD_TOO_LARGE` | > 10 МБ или текст > 1000 символов |
| 500 | `INTERNAL` | непредвиденная ошибка |

Ошибки обработки (STT/NLU/TTS) приходят **не** как HTTP-ошибка, а как событие `error`
внутри уже принятого хода, потому что `POST /messages` отвечает `202` до обработки.

Коды доменных ошибок в `error.code`:

| Код | Смысл | Реакция UI |
|---|---|---|
| `EMPTY_TRANSCRIPT` | в записи тишина | «Не расслышал, попробуйте ещё раз» |
| `STT_FAILED` | распознавание упало | предложить повторить |
| `NLU_FAILED` | разбор не удался | предложить переформулировать |
| `DEADLINE_EXCEEDED` | ход не успел за 90 с | предложить повторить |

## 6. Служебное

`GET /api/v1/health` → `200 {"status":"ok","db":"ok","scheduler":"ok"}`

## 7. Ограничения MVP

Один пользователь, нет авторизации, история диалога хранится на клиенте (сервер отдаёт
только конкретный ход). `session_id` — константа: сервер подставляет своё значение, а
присланное не проверяет; поле остаётся в контракте, чтобы потом не менять клиента.
Мультиюзер добавляется заголовком `Authorization` и middleware, подставляющим `user_id`;
форма запросов при этом не меняется.
