# deploy: инфраструктура, сервисы и web

Postgres (pgvector) + S3 (RustFS) + `TaskManager`/`stt`/`tts`/`nlu`/`web` в контейнерах.
TaskManager доступен с хоста на `127.0.0.1:5000`, web проксирует `/api/` на
`http://taskmanager:8080`.

## Запуск

```bash
cd deploy && cp .env.example .env   # Windows: copy .env.example .env
docker compose up -d --build        # s3-init и taskmanager-migrate exited (0)
docker compose ps
```

`TaskManager`, `stt`, `tts`, `nlu` и `web` собираются из `../src/TaskManager`,
`../src/stt`, `../src/tts`, `../src/nlu` и `../src/web`. Первая сборка STT скачивает
модель Whisper `base` (~150 МБ), поэтому занимает заметно больше времени. TaskManager
собирается на .NET 10; перед стартом API one-shot `taskmanager-migrate` применяет
EF-миграции.

Только инфраструктура (без приложений):

```bash
docker compose up -d postgres s3 s3-init
```

| Сервис | Адрес и креды |
|---|---|
| Postgres | `127.0.0.1:5432`, БД/юзер/пароль `todo` |
| S3 API | `http://localhost:9000`, `audio`/`tts` private, `todo-s3` / `todo-s3-secret` |
| RustFS Console | `http://localhost:9001`, те же ключи |
| STT | `http://127.0.0.1:8010` (`STT_PORT`), healthcheck `GET /health` |
| TTS | `http://127.0.0.1:8020` (`TTS_PORT`), healthcheck `GET /health` |
| NLU | `http://127.0.0.1:8001` (`NLU_PORT`), healthcheck `GET /health` |
| TaskManager | `http://127.0.0.1:5000` (`TASKMANAGER_PORT`), healthcheck `GET /api/v1/health`; внутри — `taskmanager:8080` |
| Web | `http://127.0.0.1:8080` (`WEB_PORT`), healthcheck `GET /healthz` |

Если `5432` занят (например, локальным Postgres), в `deploy/.env` поставьте
`POSTGRES_PORT=55432` и тот же порт в `DATABASE_URL` для запуска на хосте.
Контейнер TaskManager ходит в БД по внутреннему `postgres:5432`.

### NLU

NLU работает без внешних сервисов в режиме `RULES_ONLY=true` (rules-based разбор и
stub-эмбеддинг). Для полноценного разбора нужен OpenAI-совместимый API: заполните
`LLM_BASE_URL`, `LLM_API_KEY`, `LLM_MODEL` (и `EMBEDDING_*`, если эмбеддинги идут
через отдельный endpoint). Ключи хранятся только в `deploy/.env`.
TaskManager ходит на NLU по `NLU_BASE_URL`, в compose — `http://voice-nlu:8001`.
Для локального LLM на хосте: `LLM_BASE_URL=http://host.docker.internal:11434/v1`
(адрес уже проброшен через `extra_hosts`).

### TaskManager и Web

TaskManager собирается из `../src/TaskManager`, слушает `8080` внутри `todo-net`.
С хоста API доступен на `http://127.0.0.1:5000` (`TASKMANAGER_PORT`).
Перед стартом API one-shot `taskmanager-migrate` применяет EF-миграции; повторный
`up -d` идемпотентен. Если `5000` занят, измените `TASKMANAGER_PORT`.

Web проксирует `/api/` на `http://taskmanager:8080` (`TASKMANAGER_URL`).

## Проверка и сброс

```bash
docker compose exec -T postgres psql -U todo -d todo -tAc "select extversion from pg_extension where extname='vector'"
docker compose exec -T postgres psql -U todo -d todo -tAc "select count(*) from information_schema.tables where table_name = '__EFMigrationsHistory'"
curl -sI http://localhost:9000/minio/health/live | head -1          # HTTP/1.1 200 OK
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:9000/audio/  # 403: private
curl -fsS http://127.0.0.1:8010/health                              # {"status":"ok"}
curl -fsS http://127.0.0.1:8020/health                              # {"status":"ok"}
curl -fsS http://127.0.0.1:8001/health                              # {"status":"ok"}
curl -fsS http://127.0.0.1:5000/api/v1/health                       # TaskManager напрямую
curl -fsS http://127.0.0.1:8080/healthz                             # ok
curl -fsS http://127.0.0.1:8080/api/v1/health                       # TaskManager через прокси
docker compose logs -f taskmanager
docker compose down     # данные живы; down -v — полный сброс
```

Ожидается: `postgres`, `s3`, `voice-stt`, `voice-tts`, `voice-nlu`, `taskmanager`,
`web` в статусе `healthy`; `todo-s3-init` и `todo-taskmanager-migrate` завершились
с кодом `0`.

`audio` — lifecycle 7 дней; повторный `up -d` идемпотентен. Схему/таблицы создаёт
TaskManager: EF-миграции — `taskmanager-migrate`, `QRTZ_*` — при старте API; infra
создаёт только расширение `vector`.
Presigned TTL 600/900; URL подписывается на `S3_PUBLIC_HOST:S3_PUBLIC_PORT` (default `localhost:9000`), `s3:9000` — только внутри `todo-net`.
CORS: `*` (все localhost/127.0.0.1 порты, без cookies), порты только loopback.
S3 — RustFS 1.0.4, не MinIO: MinIO удалил образы из Docker Hub/quay.io в 09.2026 и урезал CE-консоль;
RustFS — Apache-2.0, S3 API, консоль 9001 и MinIO-совместимый `/minio/health/live`.
