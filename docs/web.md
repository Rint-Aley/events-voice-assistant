# Web-клиент голосового помощника

## Стек и границы

Frontend — адаптивное браузерное приложение на React, TypeScript и Vite; стили изолированы
CSS Modules, общие дизайн-токены находятся в `src/web/src/index.css`. Это статический
клиент, не отдельное iOS/Android-приложение и не PWA. Клиент не обращается к БД и не вызывает
STT/NLU/TTS напрямую: все операции идут через TaskManager по контракту
`docs/contracts/http-api.md`.

## Запуск и конфигурация

```bash
cd src/web
npm install
npm run dev
```

Для проверки и production-сборки:

```bash
npm test
npm run typecheck
npm run build
```

Базовый API URL задаётся переменной `VITE_API_BASE_URL`; по умолчанию это `/api/v1`.
Для размещения отдельно от TaskManager настройте URL API и CORS на backend, а также CORS
хранилища для presigned PUT с домена фронтенда. Микрофон в браузере доступен по HTTPS или на
`localhost`.

## Docker

```bash
docker build -t todo-web src/web
docker run --rm -p 8080:80 -e TASKMANAGER_URL=http://host.docker.internal:5000 todo-web
```

Образ двухступенчатый: Node собирает статику (`npm ci && npm run build`), nginx раздаёт
`dist` и проксирует `/api/` на `TASKMANAGER_URL`. В образе значение по умолчанию —
`http://taskmanager:8080`, а compose задаёт `host.docker.internal:5000`, пока TaskManager
запущен на хосте. `VITE_API_BASE_URL` задаётся на этапе сборки (`--build-arg`, по
умолчанию `/api/v1`); для SSE проксирование идёт без буферизации. Healthcheck —
`GET /healthz`.

Фрагмент `deploy/docker-compose.yml`:

```yaml
  web:
    build:
      context: ../src/web
      dockerfile: Dockerfile
      args:
        VITE_API_BASE_URL: /api/v1
    container_name: todo-web
    restart: unless-stopped
    ports:
      - "127.0.0.1:${WEB_PORT:-8080}:80"
    environment:
      TASKMANAGER_URL: ${TASKMANAGER_URL:-http://host.docker.internal:5000}
    extra_hosts:
      - "host.docker.internal:host-gateway"
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://127.0.0.1/healthz >/dev/null"]
      interval: 10s
      timeout: 3s
      retries: 3
```

После контейнеризации TaskManager задайте `TASKMANAGER_URL=http://taskmanager:8080`.

## Потоки

- Текст отправляется `POST /api/v1/messages`, аудио — сначала через `POST /api/v1/uploads`,
  затем напрямую в хранилище по presigned PUT, после чего `object_key` отправляется в
  `/messages`.
- Клиент подписывается на `events_url` через SSE; при разрыве соединения продолжает читать
  `GET /api/v1/turns/{turn_id}` как polling-fallback. Дубли `answer`, `audio` и `error`
  отбрасываются. История диалога сохраняется локально, поскольку сервер её не возвращает.
- Временные URL озвучки не сохраняются в истории: после перезагрузки браузера ссылка может
  истечь. Пользователь инициирует воспроизведение явным действием через нативный audio player.
- Для одного session допускается только один активный ход: пока backend обрабатывает запрос,
  поле остаётся доступным для набора, но повторная отправка выключена. `409 SESSION_BUSY`
  показывается как понятное сообщение.

## Голос и ограничения

Запись выполняется через `MediaRecorder`; перед записью клиент выбирает формат, который
поддерживает браузер и принимает API (`audio/webm`, `audio/ogg`, `audio/wav`, `audio/mpeg`).
Запись ограничена 60 секундами и 10 МБ. Клиент не конвертирует аудио молча: если допустимый
формат недоступен или пользователь не выдал разрешение на микрофон, предлагается текстовый
ввод. До отправки готового аудио API не предоставляет потоковой расшифровки, поэтому во время
записи интерфейс показывает индикатор и таймер, а не транскрипт в реальном времени.

## UI и известные ограничения

Интерфейс следует светлой теме и основным состояниям из PDF-макета. Поддержаны текстовый и
голосовой ввод, запись/обработка/ответ/уточнение/ошибка, быстрые варианты времени, карточки
результатов и воспроизведение озвучки.

В текущем API нет маршрутов получения ближайшего события отдельно от конкретного хода,
отмены действия, изменения или удаления записи. Поэтому клиент не показывает выдуманную
карточку «Ближайшее» на пустом экране и не активирует кнопку отмены из макета.
