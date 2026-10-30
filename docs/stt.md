# STT (Speech-to-Text)

Сервис распознавания речи. Принимает ссылку на аудиофайл в S3, возвращает распознанный текст. Основано на `architecture.md` §5.1 и `contracts/stt.*.json`.

## 1. Ответственность и границы
- **Делает:** скачивает аудио по presigned URL, проверяет длительность, распознает речь, отсекает тишину (VAD).
- **Не делает:** не хранит аудио, не имеет доступа к БД, не выполняет бизнес-логику, не синтезирует речь.

## 2. Вход и выход (контракты)
- **Вход:** `POST /v1/transcribe` (`stt.request.json`).
  - `turn_id` (UUID), `audio_url` (только http/https), `content_type` (webm/ogg/wav/mpeg), `max_duration_ms` (по умолчанию 60000).
- **Выход:** `stt.response.json`.
  - `text` (до 1000 символов), `language` ("ru"), `confidence` (0..1), `empty`, `duration_ms`, `provider`, `model`.
- **Ошибки:** формат `error.json`.
  - 400 `BAD_REQUEST` (невалидная схема).
  - 413 `PAYLOAD_TOO_LARGE` (файл больше 10 МБ).
  - 422 `AUDIO_TOO_LONG` (превышена длительность).
  - 422 `AUDIO_UNREADABLE` (битая ссылка, 4xx от S3 или не-аудио файл).
  - 503 `PROVIDER_UNAVAILABLE` (сеть, таймаут, 5xx).
  - 504 `DEADLINE_EXCEEDED` (превышен общий дедлайн).

## 3. Внутреннее устройство
- **Провайдер:** `faster-whisper` (модель `base`, CPU, int8).
- **Загрузка модели:** при старте через `lifespan`, healthcheck не пропускает запросы до готовности.
- **Скачивание:** стриминг с лимитом в 10 МБ (защита от OOM).
- **VAD-фильтр:** `vad_filter=True` отсекает тишину и галлюцинации Whisper.
- **Confidence:** подсчет как `exp(avg_logprob)` по всем сегментам.
- **Изоляция:** `ffprobe` и `model.transcribe` вынесены в `asyncio.to_thread`, наложен семафор на 2 параллельных запроса.
- **Идемпотентность:** in-flight дедупликация по `turn_id` и TTL-кэш на 10 минут.

## 4. Конфигурация (env)
- `STT_MODEL=base`
- `STT_DEADLINE_SECONDS=25`

## 5. Таймауты и лимиты
- Общий дедлайн - 25 секунд.
- Таймаут скачивания - 20 с, коннект - 5 с.
- Лимит аудио - 10 МБ, длительность - 120 000 мс.

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