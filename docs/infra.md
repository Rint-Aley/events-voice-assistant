# infra

Версия: 1.5. Владелец: infra. Основано на `docs/architecture.md` v0.11.

## 1. Границы

`deploy/docker-compose.yml` поднимает инфраструктуру (Postgres и S3) и все сервисы:
`TaskManager`, `stt`, `tts`, `nlu` и `web`. TaskManager слушает `8080` внутри
`todo-net`; с хоста API доступен через `TASKMANAGER_PORT`, по умолчанию `5000`.
Web проксирует `/api/` на `http://taskmanager:8080`.

Брокер не используется. Схему БД, таблицы, колонку `vector(1536)` и HNSW-индекс создаёт
TaskManager через EF-миграции; infra отвечает только за расширение `vector` и бакеты.
Модель эмбеддингов зафиксирована: `text-embedding-3-small`, `EMBEDDING_DIM=1536`.

## 2. Postgres

| Параметр | Значение |
|---|---|
| Образ | `pgvector/pgvector:0.8.7-pg17` (тег зафиксирован) |
| БД / пользователь / пароль | `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD`, локальные дефолты `todo` |
| Порт на хосте | `POSTGRES_PORT`, по умолчанию `5432`, только `127.0.0.1` |
| Данные | named volume `todo-postgres-data` |
| Время | `TZ=UTC`, `timezone=UTC`, `log_timezone=UTC` |
| Healthcheck | `pg_isready` по TCP |

Init-скрипт `deploy/postgres/init/00-extensions.sql` содержит только
`CREATE EXTENSION IF NOT EXISTS vector;` и выполняется при первом создании тома.
На существующем томе init-скрипты не перезапускаются — расширение уже установлено.

## 3. S3

| Параметр | Значение |
|---|---|
| Сервер | RustFS `1.0.4` (S3-совместимый, Apache-2.0) |
| API | `127.0.0.1:9000` (`S3_PORT`) |
| Консоль | `127.0.0.1:9001` (`S3_CONSOLE_PORT`) |
| Данные | named volume `todo-s3-data` |
| Бакеты | `audio`, `tts` (`S3_BUCKET_AUDIO`, `S3_BUCKET_TTS`) |
| Доступ | оба приватные: public access block, публичных policy/ACL нет |
| `audio` retention | lifecycle `Expiration.Days=7` (`AUDIO_RETENTION_DAYS`) |
| `tts` retention | не задаётся |

Бакеты создаёт one-shot контейнер `todo-s3-init` (`amazon/aws-cli:2.38.6`) после
`service_healthy`; повторный запуск идемпотентен (`head-bucket` → `create-bucket`).
CORS для браузерных presigned PUT/GET включён на уровне S3 API:
`RUSTFS_CORS_ALLOWED_ORIGINS` (по умолчанию `*`), методы PUT/GET/HEAD, заголовки `*`.

## 4. TaskManager

| Параметр | Значение |
|---|---|
| Образ | multi-stage `src/TaskManager/Dockerfile`, runtime `mcr.microsoft.com/dotnet/aspnet:10.0` |
| Внутренний порт | `8080` (`ASPNETCORE_HTTP_PORTS`) |
| Порт на хосте | `TASKMANAGER_PORT`, по умолчанию `5000`, только `127.0.0.1` |
| Сборка | контекст `../src/TaskManager` |
| Миграции | one-shot `taskmanager-migrate` до старта API |
| Healthcheck | `GET /api/v1/health` (Postgres + Quartz) |
| Сеть | `todo-net` |
| Зависимости | `postgres`, `s3`, `s3-init`, `voice-stt`, `voice-tts`, `voice-nlu` |

`taskmanager-migrate` собирается из target `migrations`, применяет EF-миграции и
завершается. Его строка подключения — `TASKMANAGER_CONNECTION_STRING` с внутренним
`postgres:5432`; `DATABASE_URL` из `.env` остаётся для запуска на хосте и EF-команд.
Повторный `docker compose up -d` идемпотентен: EF пропускает уже применённые миграции.

TaskManager монтирует только `todo-net`; томов нет. `QRTZ_*` таблицы Quartz создаёт
при старте API. Для корректного завершения фоновых задач compose задаёт
`stop_grace_period: 30s`.

## 5. Presigned URL

- Ссылки подписываются на `S3_PUBLIC_HOST:S3_PUBLIC_PORT`, по умолчанию
  `localhost:9000`. Это адрес, который увидит потребитель ссылки (браузер или
  `stt`), а не внутренний адрес TaskManager.
- `Host` входит в подпись SigV4. Готовый URL нельзя переписывать на `s3:9000`
  или `127.0.0.1` — подпись перестанет совпадать.
- `s3:9000` (`S3_INTERNAL_ENDPOINT`) — адрес S3 внутри `todo-net`. Им пользуется
  контейнер `tts` для загрузки файлов; в presigned URL это имя не отдаётся.
- TaskManager внутри контейнера получает `S3_ENDPOINT=http://localhost:9000` и только
  подписывает URL. Сетевых вызовов S3 из TaskManager нет, поэтому `s3:9000` ему не нужен.
- `stt` получает presigned URL от TaskManager. Сейчас ссылка подписывается на
  `localhost:9000`, поэтому контейнеру `voice-stt` добавлен
  `extra_hosts: localhost:host-gateway`: имя `localhost` внутри контейнера указывает
  на хост, и подпись SigV4 (включая `Host`) совпадает.
- Альтернатива на будущее — отдельные presigned URL на потребителя: браузеру —
  `localhost:9000`, сервисам — `s3:9000`; один URL на всех без маппинга `localhost`
  не подходит.
- TTL: upload 600 с, download 900 с. После истечения S3 отвечает 403, объект остаётся.
- `content_type` объекта определяется по расширению `object_key`; `HeadObject` не вызывается.
  Неизвестное или отсутствующее расширение — `application/octet-stream`.
- Клиенты S3 используют path-style addressing и регион `us-east-1`.
- Браузер всегда получает presigned URL; секреты S3 в браузер не передаются.

Альтернатива для будущего: выдать `stt`/`tts` собственные ограниченные ключи и ходить
в S3 напрямую. Это убирает подпись для внутренних вызовов, но требует IAM-политик,
ротации и меняет контракт (`audio_url` → bucket/key). Для браузера presigned URL
останется в любом случае. Решение отложено.

## 6. Конфигурация

Все переменные и локальные дефолты — в `deploy/.env.example`. Compose читает
`POSTGRES_*`, `S3_PORT`, `S3_CONSOLE_PORT`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`,
`S3_REGION`, `S3_BUCKET_*`, `S3_CORS_ALLOWED_ORIGINS`, `AUDIO_RETENTION_DAYS`,
`STT_PORT`, `STT_MODEL`, `STT_DEADLINE_SECONDS`, `STT_LANGUAGE`, `TTS_PORT`,
`TTS_DEFAULT_VOICE`, `TTS_DEFAULT_FORMAT`, `TTS_TIMEOUT_SECONDS`, `NLU_PORT`, `LLM_*`,
`EMBEDDING_*`, `RULES_ONLY`, `TIMEZONE`, `WEB_PORT`, `TASKMANAGER_PORT`,
`TASKMANAGER_URL`.
`S3_PUBLIC_*`, `S3_ENDPOINT`, `S3_INTERNAL_ENDPOINT`, `DATABASE_URL` и TTL — контракт
для конфигов приложений; внутри контейнера `tts` используется `S3_ENDPOINT=s3:9000`,
а TaskManager — публичный `S3_ENDPOINT` для подписи. Строка подключения TaskManager
в контейнере задаётся через `TASKMANAGER_CONNECTION_STRING`.

## 7. Проверка

```bash
docker compose -f deploy/docker-compose.yml config --quiet
cd deploy && docker compose up -d --build && docker compose ps
docker compose exec -T postgres psql -U todo -d todo -tAc "select extversion from pg_extension where extname='vector'"
docker compose exec -T postgres psql -U todo -d todo -tAc "select count(*) from information_schema.tables where table_name = '__EFMigrationsHistory'"
curl -sI http://localhost:9000/minio/health/live
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:9000/audio/
curl -fsS http://127.0.0.1:8010/health
curl -fsS http://127.0.0.1:8020/health
curl -fsS http://127.0.0.1:8001/health
curl -fsS http://127.0.0.1:5000/api/v1/health
curl -fsS http://127.0.0.1:8080/healthz
curl -fsS http://127.0.0.1:8080/api/v1/health
docker compose logs -f taskmanager
docker compose down -v
```

Ожидается: compose без ошибок; `postgres`, `s3`, `voice-stt`, `voice-tts`, `voice-nlu`,
`taskmanager`, `web` в статусе `healthy`; `todo-s3-init` и `todo-taskmanager-migrate`
завершились с кодом `0`; версия `vector` непустая; health S3 — `200`; анонимный доступ
к `audio` — `403`; health STT/TTS/NLU — `{"status":"ok"}`; TaskManager напрямую и через
web отвечает `200`; `/healthz` web — `ok`.

## 8. Отложено

- демо по LAN или HTTPS: нужен внешний IP/домен, публикация портов не только на loopback,
  TLS или reverse proxy;
- общие ключи S3 для `stt`/`tts` вместо presigned URL (см. §5);
- EF migration bundle вместо SDK target для `taskmanager-migrate`;
- разделение public/internal endpoint для потребителей presigned URL;
- production-hardening: секреты вне `.env`, ротация ключей, ограниченные IAM-политики.
