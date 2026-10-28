# infra

Версия: 1. Владелец: infra. Основано на `docs/architecture.md` v0.8.

## 1. Границы

`deploy/docker-compose.yml` поднимает только зависимости: Postgres и S3.
Приложения (`web`, `TaskManager`, `stt`, `nlu`, `tts`) запускаются локально и ходят
на `localhost`; в будущем они могут быть упакованы в контейнеры без изменения контрактов.

Брокер не используется. Схему БД, таблицы, `vector(1024)` и HNSW-индекс создаёт TaskManager;
infra отвечает только за расширение `vector` и бакеты.

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

## 4. Presigned URL

- Ссылки подписываются на `S3_PUBLIC_HOST:S3_PUBLIC_PORT`, по умолчанию
  `localhost:9000`. Это адрес, который увидит потребитель ссылки (браузер или локальный
  `stt`/`tts`), а не TaskManager.
- `Host` входит в подпись SigV4. Готовый URL нельзя переписывать на `s3:9000`
  или `127.0.0.1` — подпись перестанет совпадать.
- `s3:9000` (`S3_INTERNAL_ENDPOINT`) — только для будущих контейнеров внутри `todo-net`.
  Наружу и в presigned URL это имя не отдаётся.
- Если позже TaskManager останется на хосте, а `stt`/`tts` уедут в контейнеры,
  генерируются отдельные presigned URL на потребителя: браузеру — `localhost:9000`,
  сервисам — `s3:9000`. Один URL на всех не подходит.
- TTL: upload 600 с, download 900 с. После истечения S3 отвечает 403, объект остаётся.
- Клиенты S3 используют path-style addressing и регион `us-east-1`.
- Браузер всегда получает presigned URL; секреты S3 в браузер не передаются.

Альтернатива для будущего: выдать `stt`/`tts` собственные ограниченные ключи и ходить
в S3 напрямую. Это убирает подпись для внутренних вызовов, но требует IAM-политик,
ротации и меняет контракт (`audio_url` → bucket/key). Для браузера presigned URL
останется в любом случае. Решение отложено.

## 5. Конфигурация

Все переменные и локальные дефолты — в `deploy/.env.example`. Compose читает
`POSTGRES_*`, `S3_PORT`, `S3_CONSOLE_PORT`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`,
`S3_REGION`, `S3_BUCKET_*`, `S3_CORS_ALLOWED_ORIGINS`, `AUDIO_RETENTION_DAYS`.
`S3_PUBLIC_*`, `S3_ENDPOINT`, `S3_INTERNAL_ENDPOINT`, `DATABASE_URL` и TTL — контракт
для конфигов приложений; compose их не использует.

## 6. Проверка

```bash
docker compose -f deploy/docker-compose.yml config --quiet
cd deploy && docker compose up -d && docker compose ps
docker compose exec -T postgres psql -U todo -d todo -tAc "select extversion from pg_extension where extname='vector'"
curl -sI http://localhost:9000/minio/health/live
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:9000/audio/
docker compose down -v
```

Ожидается: compose без ошибок; оба сервиса `healthy` меньше чем за 60 с; версия `vector`
непустая; health `200`; анонимный доступ к `audio` — `403`.

## 7. Отложено

- демо по LAN или HTTPS: нужен внешний IP/домен, публикация портов не только на loopback,
  TLS или reverse proxy;
- общие ключи S3 для `stt`/`tts` вместо presigned URL (см. §4);
- контейнеризация приложений и разделение public/internal endpoint на consumers;
- production-hardening: секреты вне `.env`, ротация ключей, ограниченные IAM-политики.
