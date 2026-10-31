# deploy: инфраструктура, сервисы и web

Postgres (pgvector) + S3 (RustFS) + `stt`/`tts`/`nlu`/`web` в контейнерах. `TaskManager`
пока запускается на хосте; web проксирует к нему `/api/`.

## Запуск

```bash
cd deploy && cp .env.example .env   # Windows: copy .env.example .env
docker compose up -d --build        # postgres/s3 healthy, s3-init exited (0)
docker compose ps
```

`stt`, `tts`, `nlu` и `web` собираются из `../src/stt`, `../src/tts`, `../src/nlu`
и `../src/web`. Первая сборка STT скачивает модель Whisper `base` (~150 МБ), поэтому
занимает заметно больше времени.

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
| Web | `http://127.0.0.1:8080` (`WEB_PORT`), healthcheck `GET /healthz` |

Если `5432` занят (например, локальным Postgres), в `deploy/.env` поставьте
`POSTGRES_PORT=55432` и тот же порт в `DATABASE_URL`.

### NLU

NLU работает без внешних сервисов в режиме `RULES_ONLY=true` (rules-based разбор и
stub-эмбеддинг). Для полноценного разбора нужен OpenAI-совместимый API: заполните
`LLM_BASE_URL`, `LLM_API_KEY`, `LLM_MODEL` (и `EMBEDDING_*`, если эмбеддинги идут
через отдельный endpoint). Ключи хранятся только в `deploy/.env`.
TaskManager ходит на NLU по `NLU_BASE_URL`, по умолчанию `http://localhost:8001`.
Для локального LLM на хосте: `LLM_BASE_URL=http://host.docker.internal:11434/v1`
(адрес уже проброшен через `extra_hosts`).

### Web и TaskManager

По умолчанию `TASKMANAGER_URL=http://host.docker.internal:5000` — web проксирует
`/api/` на TaskManager, запущенный на хосте. Если TaskManager слушает другой порт,
поправьте `TASKMANAGER_URL` в `deploy/.env`. После контейнеризации TaskManager
поставьте `TASKMANAGER_URL=http://taskmanager:8080`.

На Linux TaskManager должен слушать `0.0.0.0`, а не только `localhost`, иначе
контейнер до него не достучится.

## Проверка и сброс

```bash
docker compose exec -T postgres psql -U todo -d todo -tAc "select extversion from pg_extension where extname='vector'"
curl -sI http://localhost:9000/minio/health/live | head -1          # HTTP/1.1 200 OK
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:9000/audio/  # 403: private
curl -fsS http://127.0.0.1:8010/health                              # {"status":"ok"}
curl -fsS http://127.0.0.1:8020/health                              # {"status":"ok"}
curl -fsS http://127.0.0.1:8001/health                              # {"status":"ok"}
curl -fsS http://127.0.0.1:8080/healthz                             # ok
curl -fsS http://127.0.0.1:8080/api/v1/health                       # TaskManager через прокси
docker compose down     # данные живы; down -v — полный сброс
```

`audio` — lifecycle 7 дней; повторный `up -d` идемпотентен. Схему/таблицы infra не создаёт — только `vector`.
Presigned TTL 600/900; URL подписывается на `S3_PUBLIC_HOST:S3_PUBLIC_PORT` (default `localhost:9000`), `s3:9000` — только внутри `todo-net`.
CORS: `*` (все localhost/127.0.0.1 порты, без cookies), порты только loopback.
S3 — RustFS 1.0.4, не MinIO: MinIO удалил образы из Docker Hub/quay.io в 09.2026 и урезал CE-консоль;
RustFS — Apache-2.0, S3 API, консоль 9001 и MinIO-совместимый `/minio/health/live`.
