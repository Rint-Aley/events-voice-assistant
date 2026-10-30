# TaskManager backend — инструкции для агентов

## Что это

Монорепозиторий голосового ассистента. Бэкенд — `TaskManager` на C# / ASP.NET Core.
Бэкенд ещё не написан: сначала спецификации, потом код. Работаем по SDD, маленькими
задачами, каждая задача — одна сессия и один коммит.

## Источники истины

- `docs/task-manager/implementation.md` — как писать бэкенд: стек, проекты, слои,
  EF Core, Quartz, API, тесты.
- `docs/task-manager/task-manager.md` — устройство сервиса: состояния, переходы,
  фоновые задачи, таблицы, SSE.
- `docs/contracts/` — HTTP и JSON-контракты между сервисами.
- `docs/assistant_replies.md` — авторитетные тексты ответов пользователю.
- `docs/task-manager/prompts.md` — готовые промпты по задачам.

Если `implementation.md` и `task-manager.md` расходятся, для кода бэкенда приоритет у
`implementation.md`. Если код требует изменить контракт — остановись и спроси.

## Команды

```bash
dotnet build src/TaskManager/TaskManager.sln
dotnet test src/TaskManager/TaskManager.sln
git diff --check
```

Для миграций:

```bash
dotnet ef migrations add <Name> --project src/TaskManager/TaskManager.Infrastructure --startup-project src/TaskManager/TaskManager.Api
dotnet ef database update --project src/TaskManager/TaskManager.Infrastructure --startup-project src/TaskManager/TaskManager.Api
```

## Архитектурные границы

Корень бэкенда — `src/TaskManager/`. В `src/` рядом лежат `tts`, `stt`, `nlu` и `web`,
поэтому проекты бэкенда не создаются прямо в `src/`.

- `src/TaskManager/TaskManager.SharedKernel` — `Result`, ошибки, `IClock`, идентификаторы.
  Без EF Core, ASP.NET, Quartz и HTTP-клиентов.
- `src/TaskManager/TaskManager.Application.Abstractions` — общие порты: `IUnitOfWork`,
  `IObjectStorage`, `IJobScheduler`, `ITurnStream`, `IIdGenerator`.
- `src/TaskManager/Modules/Turns/Turns.Domain` — состояние хода, переходы, черновик, ответ.
- `src/TaskManager/Modules/Turns/Turns.Application` — сценарии Turns, порты, политика
  фоновых задач.
- `src/TaskManager/Modules/Events/Events.Domain` — событие, статусы, конфликты, правила
  поиска и списка.
- `src/TaskManager/Modules/Events/Events.Application` — создание, выборка, поиск, порты
  Events.
- `src/TaskManager/TaskManager.Infrastructure` — EF Core, репозитории, миграции, Quartz,
  HTTP-клиенты, S3.
- `src/TaskManager/TaskManager.Api` — endpoints, DI, конфигурация, SSE.

Зависимости только в одну сторону:

```text
Api ─┐
     ├──► Turns.Application ──► Turns.Domain
Infra┘          │
                └──► Events.Application ──► Events.Domain
```

Правила:

- `Events.*` не импортирует `Turns.*`.
- `Turns.Domain` не импортирует `Events.*`.
- `Turns.Application` может зависеть от `Events.Application` и `Events.Domain`.
- Domain не знает про EF Core, ASP.NET, Quartz, `HttpClient`, JSON-контракты и `ILogger`.
- Только Application-сценарии открывают и коммитят транзакции.
- Один `TaskManagerDbContext`, один migration history, без DAL-проекта.
- Без generic repository и без AutoMapper.
- SQL живёт только в EF-репозиториях и query-портах.

## Контракты и совместимость

- `nlu.request` не меняется.
- В `nlu.response` можно добавлять только необязательные поля. Сейчас это `type_filter`
  и `is_deadline`. Новые значения `action` не добавляются.
- Новый TaskManager должен работать со старым NLU: отсутствующие `type_filter` и
  `is_deadline` означают «все типы» и `false`.
- HTTP-контракт web ↔ TaskManager можно расширять только необязательными полями.
- Не переименовывать коды ошибок и не менять формат `error.json`.
- Не менять `docs/contracts/` и `docs/assistant_replies.md` без явного разрешения.

## Данные и время

- Одна база Postgres, расширение `pgvector`.
- `events.starts_at` — локальное `time` без зоны, интерпретируется в `TIMEZONE`.
- `events.starts_on` — `date`.
- `events.description` — обязательный заголовок для всех типов.
- `events.source_text` — исходные фразы пользователя, не участвует в эмбеддинге.
- `events.is_deadline` — только для `task`.
- `EMBEDDING_MODEL` и `EMBEDDING_DIM` — `TBD(BLOCKING)`.
- До фиксации модели не создавать `vector(N)` и HNSW-индекс.
- `description` без валидного `embedding` — `NLU_FAILED`, событие не создаётся.

## Поведение, которое нельзя ломать

- Заголовок обязателен для task, reminder и meeting.
- Reminder и meeting требуют дату и время; meeting по умолчанию 60 минут.
- Task может быть без даты и времени.
- Уточнений не больше двух раундов; после второго — `COMPLETED`, текст
  «Не удалось уточнить. Сформулируй запрос иначе.», черновик удаляется.
- Невалидная дата/время: слот удаляется, черновик сохраняется, добавляется `missing`,
  задаётся общий вопрос.
- Прошлое время: clarify с сохранением черновика.
- Отмена черновика: `DELETE /api/v1/turns/{id}/draft`, идемпотентно,
  `200 {"text":"Хорошо, не сохраняю"}`, `404` для неизвестного хода.
- Черновик сбрасывается на любом интенте, кроме `create_*` и `clarify`.
- Поиск и список: до 3 записей, `active + done`, `cancelled` исключён.
- Список: `starts_on`, затем `starts_at`; без времени — в конце дня; задачи без даты —
  после датированных, блок «Без даты». Сейчас лимит 3 суммарно, включая «Без даты».
- `type_filter`: `meeting` — встречи; `task` — задачи и напоминания; `reminder` —
  напоминания; отсутствует — все.
- Конфликты встреч: только `meeting`/`active`, интервал `[start, start + duration)`,
  конец не входит. Встреча сохраняется всегда, `warnings` содержит все пересечения.
- SSE: `answer` сразу, `audio` позже; соединение держится до 30 секунд после ответа.
- Ошибка TTS не переводит ход в `FAILED`: только `audio_status = failed`.

## Тесты

- Unit-тесты — только Domain: `Turns.Domain.Tests`, `Events.Domain.Tests`.
- Всё остальное — интеграционные тесты.
- Утверждения — FluentAssertions (`.Should()`), без xUnit `Assert`.
- БД в интеграционных тестах — реальный Postgres через Testcontainers.
- `stt`, `nlu`, `tts`, S3 — NSubstitute/fakes.
- Планировщик в тестах Application — fake `IJobScheduler`; Quartz-адаптер проверяется
  отдельным тестом с in-memory store.
- Семантический поиск с `vector(N)` блокируется до фиксации embedding-модели.

## Правила работы агента

- Не коммить изменения: коммит делает человек.
- Не менять файлы вне scope задачи.
- Не добавлять пакеты без согласования.
- Не писать «заглушки», которые выглядят как рабочая логика.
- Сначала кратко перечисли файлы, которые собираешься создать или изменить.
- Если нужно изменить контракт, добавить пакет или принять архитектурное решение —
  остановись и спроси.
- Если задача разрастается больше ~500–700 строк diff или трогает больше 2–3 проектов —
  предложи разбить её.
- В конце задачи отчитайся: изменённые файлы, что сделано, результаты
  `dotnet build src/TaskManager/TaskManager.sln`,
  `dotnet test src/TaskManager/TaskManager.sln`, `git diff --check`, что осталось или
  заблокировано.

## Definition of done

- `dotnet build src/TaskManager/TaskManager.sln` проходит.
- `dotnet test src/TaskManager/TaskManager.sln` проходит.
- `git diff --check` без ошибок.
- Нет изменений контрактов без разрешения.
- Нет `vector(N)` и HNSW до фиксации `EMBEDDING_MODEL`/`EMBEDDING_DIM`.
- Тесты покрывают поведение, а не только happy path.
- Документация не противоречит коду; если противоречие найдено — сообщить, а не
  молча менять docs.
