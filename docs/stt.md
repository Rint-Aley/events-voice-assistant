# STT (Speech-to-Text)

Сервис распознавания речи. Принимает ссылку на аудиофайл в S3, возвращает распознанный текст. Основано на `architecture.md` §5.1 и `contracts/stt.*.json`.

## 1. Ответственность и границы
- **Делает:** скачивает аудио по presigned URL, проверяет длительность, распознает речь, отсекает тишину (VAD), возвращает текст и уверенность.
- **Не делает:** не хранит аудио, не имеет доступа к БД, не выполняет бизнес-логику, не синтезирует речь.

## 2. Вход и выход (контракты)
- **Вход:** `POST /v1/transcribe` (`stt.request.json`).
  - `turn_id` (UUID), `audio_url` (только http/https), `content_type` (webm/ogg/wav/mpeg), `max_duration_ms` (по умолчанию 60000, максимум 120000), `language` (только "ru").
- **Выход:** `stt.response.json`.
  - `text` (до 1000 символов), `language` ("ru"), `confidence` (0..1), `empty`, `duration_ms`, `provider`, `model`.
- **Ошибки:** формат `error.json`.
  - 400 `BAD_REQUEST` - невалидная схема запроса.
  - 413 `PAYLOAD_TOO_LARGE` - файл больше 10 МБ.
  - 422 `AUDIO_TOO_LONG` - превышена длительность.
  - 422 `AUDIO_UNREADABLE` - битая ссылка (4xx от S3), пустой файл или файл не является аудио.
  - 503 `PROVIDER_UNAVAILABLE` - сетевой сбой, таймаут или 5xx от S3.
  - 504 `DEADLINE_EXCEEDED` - превышен общий дедлайн обработки.

## 3. Внутреннее устройство
- **Провайдер:** `faster-whisper` (модель `base`, CPU, int8).
- **Загрузка модели:** выполняется в `lifespan`. Healthcheck не пропускает трафик, пока модель не загружена в память.
- **Скачивание:** реализовано через стриминг с жестким лимитом 10 МБ (защита от OOM). Поддерживает редиректы (`follow_redirects=True`). При превышении лимита возвращается `413`.
- **Проверка длительности:** `ffprobe` вызывается до запуска Whisper. Если `ffprobe` не может прочитать файл, возвращается `AUDIO_UNREADABLE` без запуска нейросети (экономия CPU).
- **VAD-фильтр:** параметр `vad_filter=True` отсекает тишину и предотвращает галлюцинации Whisper.
- **Confidence:** рассчитывается как экспонента от среднего логарифма вероятности (`exp(avg_logprob)`), результат ограничивается диапазоном [0, 1].
- **Изоляция:** вызовы `ffprobe` и `model.transcribe` обернуты в `asyncio.to_thread`. Для защиты от зомби-потоков при таймауте наложен семафор `asyncio.Semaphore(2)`.
- **Идемпотентность:** реализована in-flight дедупликация по `turn_id` (второй запрос ждет завершения первого) и LRU-кэш (`OrderedDict`) на 1000 записей с TTL 10 минут. Кэш очищается фоновым таском.
- **Валидация:** строгая Pydantic-схема запроса (`extra="forbid"`) с проверкой типов и лимитов.
- **Docker:** модель `base` предзагружается на этапе сборки образа; при старте она загружается в память, healthcheck не пропускает трафик до готовности сервиса.

## 4. Конфигурация (env)
- `STT_MODEL=base` - выбор модели.
- `STT_DEADLINE_SECONDS=25` - общий дедлайн выполнения.

## 5. Таймауты и лимиты
- Общий дедлайн на весь запрос - 25 секунд. При превышении возвращается 504.
- Таймаут скачивания аудио - 20 с, коннект - 5 с.
- Лимит размера файла - 10 МБ.
- Максимальная длительность аудио - 120 000 мс.
- Текст обрезается до 1000 символов по границе слова (без разрыва слов).

## 6. Как проверить
```bash
# Успешный запрос
curl -X POST http://127.0.0.1:8010/v1/transcribe \
  -H "Content-Type: application/json" \
  -d '{"turn_id":"11111111-1111-1111-1111-111111111111","audio_url":"http://host.docker.internal:8001/test.wav","content_type":"audio/wav"}'

# Битая ссылка (ожидается 422 AUDIO_UNREADABLE)
curl -X POST http://127.0.0.1:8010/v1/transcribe \
  -H "Content-Type: application/json" \
  -d '{"turn_id":"11111111-1111-1111-1111-111111111111","audio_url":"http://host.docker.internal:8001/missing.wav","content_type":"audio/wav"}'
```

## 7. Сборка и запуск
- **Исходники и файлы сборки:** `src/stt/` (`app.py`, `requirements.txt`, `Dockerfile`, `.dockerignore`).
- **Порт:** `8010`.
- **Docker-образ:** `docker build -t todo-stt src/stt`, запуск — `docker run -p 8010:8010 todo-stt`.
- **Локальный запуск:** `pip install -r src/stt/requirements.txt`, затем `uvicorn app:app --app-dir src/stt --port 8010`. Нужны установленные `ffmpeg` и `ffprobe`.
- **Через compose:** из `deploy/` — `docker compose up -d --build voice-stt`; Postgres и S3 поднимутся по `depends_on`. Проверка — `curl -fsS http://127.0.0.1:8010/health`.