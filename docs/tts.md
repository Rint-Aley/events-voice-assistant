# TTS (Text-to-Speech)

Сервис синтеза речи. Принимает текст, генерирует аудиофайл, загружает его в S3 (RustFS) и возвращает ключ объекта. Основано на `architecture.md` §5.3 и `contracts/tts.*.json`.

## 1. Ответственность и границы
- **Делает:** синтезирует речь из текста, конвертирует в нужный формат, сохраняет в S3, кэширует результаты по хэшу.
- **Не делает:** не генерирует текст, не имеет доступа к БД, не знает контекста диалога.

## 2. Вход и выход (контракты)
- **Вход:** `POST /v1/synthesize` (`tts.request.json`).
  - `answer_id` (UUID), `text` (1-1000 символов), `voice`, `format` (mp3/ogg_opus/wav), `speed` (0.5..2.0).
- **Выход:** `tts.response.json`.
  - `object_key` (путь в S3 вида `tts/<sha256>.mp3`), `content_type`, `format`, `duration_ms`, `cached`.
- **Ошибки:** формат `error.json`.
  - 400 `BAD_REQUEST` (невалидная схема).
  - 422 `EMPTY_TEXT` (текст пустой или состоит из пробелов).
  - 422 `UNSUPPORTED_FORMAT` (ошибка конвертации ffmpeg).
  - 503 `PROVIDER_UNAVAILABLE` (edge-tts, S3 или ffmpeg недоступны).
  - 504 `DEADLINE_EXCEEDED` (превышен общий таймаут).

## 3. Внутреннее устройство
- **Провайдер:** `edge-tts` (голос `ru-RU-SvetlanaNeural`).
- **Хранилище:** S3 (RustFS). Клиент `boto3` с `signature_version="s3v4"` и таймаутами.
- **Кэширование:** Ключ детерминирован. Хэш: `sha256(text + \x1f + voice + \x1f + format + \x1f + speed)`. Разделитель `\x1f` защищает от коллизий.
- **Дедупликация:** Проверка файла через `head_object`. Если файл есть, длительность берется из метаданных S3 (или через ffprobe при их отсутствии).
- **Конвертация:** `ffmpeg` (в `asyncio.to_thread`). Защищена таймаутом.
- **Изоляция:** Синхронизация параллельных запросов через `asyncio.Lock` на хэш. Ограничение одновременных синтезов через семафор (3 шт).

## 4. Конфигурация (env)
- `TTS_DEFAULT_VOICE=ru-RU-SvetlanaNeural`
- `TTS_DEFAULT_FORMAT=mp3`
- `TTS_TIMEOUT_SECONDS=20`
- `S3_ENDPOINT`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_BUCKET_TTS`, `S3_REGION`

## 5. Таймауты и лимиты
- Общий таймаут на весь запрос (синтез + конвертация + загрузка в S3) - 20 секунд (настраивается через `TTS_TIMEOUT_SECONDS`). При превышении возвращается 504 `DEADLINE_EXCEEDED`.
- Внутренний таймаут синтеза (edge-tts) - 15 секунд.
- Внутренний таймаут конвертации (ffmpeg) - 10 секунд.
- Лимит текста - 1000 символов.

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