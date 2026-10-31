# TTS (Text-to-Speech)

Сервис синтеза речи. Принимает текст, генерирует аудиофайл, загружает его в S3 (RustFS) и возвращает ключ объекта. Основано на `architecture.md` §5.3 и `contracts/tts.*.json`.

## 1. Ответственность и границы
- **Делает:** синтезирует речь из текста, конвертирует в нужный формат (mp3, ogg, wav), сохраняет в S3, кэширует результаты по хэшу.
- **Не делает:** не генерирует текст, не имеет доступа к БД, не знает контекста диалога.

## 2. Вход и выход (контракты)
- **Вход:** `POST /v1/synthesize` (`tts.request.json`).
  - `answer_id` (UUID), `text` (1-1000 символов), `voice`, `format` (mp3/ogg_opus/wav), `speed` (0.5..2.0).
- **Выход:** `tts.response.json`.
  - `object_key` (путь в S3 вида `tts/<sha256>.mp3`), `content_type`, `format`, `duration_ms`, `cached`.
- **Ошибки:** формат `error.json`.
  - 400 `BAD_REQUEST` - невалидная схема запроса.
  - 422 `EMPTY_TEXT` - текст пустой или состоит только из пробелов.
  - 422 `UNSUPPORTED_FORMAT` - ошибка конвертации ffmpeg.
  - 503 `PROVIDER_UNAVAILABLE` - edge-tts, S3 или ffmpeg недоступны, либо превышен внутренний таймаут.
  - 504 `DEADLINE_EXCEEDED` - превышен общий таймаут запроса.

## 3. Внутреннее устройство
- **Провайдер:** `edge-tts` (голос `ru-RU-SvetlanaNeural`). Поддерживается изменение скорости.
- **Хранилище:** S3 (RustFS). Клиент `boto3` настроен с `signature_version="s3v4"`, таймаутами (`connect_timeout=2`, `read_timeout=5`) и отключенными автоматическими ретраями.
- **Кэширование:** Ключ детерминирован: `sha256(text + \x1f + voice + \x1f + format + \x1f + speed)`. Символ `\x1f` защищает от коллизий, а включение `format` и `speed` гарантирует, что ускоренная версия или другой формат не вернут старый файл.
- **Дедупликация:** Проверка существования файла через `head_object`. Если файл есть, длительность берется из метаданных S3. Если метаданных нет, файл скачивается и прогоняется через `ffprobe`. Также проверяется `Content-Type` во избежание выдачи битого файла.
- **Конвертация:** Выполняется через `ffmpeg` (в `asyncio.to_thread`). Для `ogg_opus` явно указывается контейнер (`-f ogg`). Ошибки `CalledProcessError` перехватываются и возвращают 422.
- **Изоляция:** Параллельные запросы с одинаковым хэшем синхронизируются через `asyncio.Lock`. Одновременные вызовы ffmpeg/edge-tts ограничены семафором `asyncio.Semaphore(3)`. Словарь локов очищается фоновым таском.
- **Очистка:** Временные файлы удаляются в блоке `finally` при любом исходе (успех или ошибка).
- **Валидация:** строгая Pydantic-схема запроса (`extra="forbid"`) с проверкой типов и лимитов.

## 4. Конфигурация (env)
- `TTS_DEFAULT_VOICE=ru-RU-SvetlanaNeural`
- `TTS_DEFAULT_FORMAT=mp3`
- `TTS_TIMEOUT_SECONDS=20`
- `S3_ENDPOINT`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_BUCKET_TTS`, `S3_REGION` - доступы к хранилищу.

## 5. Таймауты и лимиты
- Общий таймаут на весь запрос (синтез + конвертация + загрузка в S3) - 20 секунд. При превышении возвращается 504.
- Внутренний таймаут синтеза (edge-tts) - 15 секунд.
- Внутренний таймаут конвертации (ffmpeg) - 10 секунд.
- Лимит длины текста - 1000 символов.

## 6. Как проверить
```bash
# Успешный запрос
curl -X POST http://127.0.0.1:8020/v1/synthesize \
  -H "Content-Type: application/json" \
  -d '{"answer_id":"22222222-2222-2222-2222-222222222222","text":"Готово: напоминание на завтра, 10:00","voice":"ru-RU-SvetlanaNeural","format":"mp3"}'

# Повторный запрос (ожидается "cached": true)
curl -X POST http://127.0.0.1:8020/v1/synthesize \
  -H "Content-Type: application/json" \
  -d '{"answer_id":"22222222-2222-2222-2222-222222222222","text":"Готово: напоминание на завтра, 10:00","voice":"ru-RU-SvetlanaNeural","format":"mp3"}'
```

## 7. Сборка и запуск
- **Исходники и файлы сборки:** `src/tts/` (`app.py`, `requirements.txt`, `Dockerfile`, `.dockerignore`).
- **Порт:** `8020`.
- **Docker-образ:** `docker build -t todo-tts src/tts`, запуск — `docker run -p 8020:8020 -e S3_ENDPOINT=... todo-tts`.
- **Локальный запуск:** `pip install -r src/tts/requirements.txt`, затем `uvicorn app:app --app-dir src/tts --port 8020`. Нужны доступный S3 и переменные `S3_ENDPOINT`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_BUCKET_TTS`, `S3_REGION`.
- **Через compose:** из `deploy/` — `docker compose up -d --build voice-tts`; S3 и `s3-init` поднимутся по `depends_on`. Проверка — `curl -fsS http://127.0.0.1:8020/health`.