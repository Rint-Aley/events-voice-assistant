# TaskManager: реализация бэкенда

Версия: 1. Владелец: бэкенд. Статус: рабочая спецификация для реализации.

Документ описывает, **как писать** TaskManager: стек, структуру solution, границы проектов,
EF Core, Quartz 4.x, API/SSE и тестовую стратегию. Устройство сервиса — FSM, таблицы, потоки —
в `task-manager.md`; форматы обмена — в `../contracts/`.

Правило приоритета: для кода бэкенда этот файл важнее `task-manager.md`, если они расходятся.
`task-manager.md` остаётся источником подробностей по состояниям и данным.

---

## 1. Границы

TaskManager — один деплой-юнит и модульный монолит. Внутри два модуля:

- **Turns** — ход диалога, состояния, черновик, ответ, политика фоновых задач;
- **Events** — задачи, напоминания, встречи, конфликты, поиск и выборка.

Общие адаптеры:

- **Api** — HTTP, SSE, коды ответов;
- **Infrastructure** — EF Core, Quartz, HTTP-клиенты, S3, presigned URL.

TaskManager не распознаёт речь, не разбирает текст и не синтезирует аудио. Он вызывает
`stt`/`nlu`/`tts`, хранит единственный источник правды о записях и отдаёт состояние хода.

---

## 2. Стек

| Слой | Технология |
|---|---|
| Язык | C# 12+ |
| Платформа | .NET 8 LTS или более новый LTS |
| Web | ASP.NET Core |
| ORM | EF Core + Npgsql |
| Векторы | pgvector |
| Планировщик | Quartz.NET 4.x |
| Тесты | xUnit + NSubstitute + FluentAssertions |
| БД в тестах | Testcontainers + Postgres/pgvector |
| JSON | System.Text.Json |
| Логи | `Microsoft.Extensions.Logging` |

Запрещено:

- отдельный DAL-проект: DAL — это EF Core внутри `TaskManager.Infrastructure`;
- отдельный `DbContext` на модуль: контекст один;
- generic repository;
- AutoMapper и другие мапперы;
- `Thread.Sleep` и собственные retry-циклы;
- EF Core, ASP.NET, Quartz и HTTP-клиенты в Domain;
- бизнес-логика в контроллерах и EF-конфигурациях.

---

## 3. Структура solution

```
src/
  TaskManager/
    TaskManager.sln
    TaskManager.SharedKernel/
    TaskManager.Application.Abstractions/
    Modules/
      Turns/
        Turns.Domain/
        Turns.Application/
      Events/
        Events.Domain/
        Events.Application/
    TaskManager.Infrastructure/
    TaskManager.Api/
    tests/
      Modules/
        Turns/
          Turns.Domain.Tests/
        Events/
          Events.Domain.Tests/
      TaskManager.IntegrationTests/
  tts/
  stt/
  nlu/
  web/
```

`src/TaskManager/` — корень бэкенда. Рядом в `src/` лежат остальные сервисы
(`tts`, `stt`, `nlu`, `web`), поэтому проекты бэкенда нельзя создавать прямо в `src/`.

Правила раскладки:

- `TaskManager.SharedKernel` — примитивы: `Result`, `DomainError`, `IClock`, идентификаторы,
  общие value objects. Без EF Core, ASP.NET и Quartz.
- `TaskManager.Application.Abstractions` — общие порты: `IUnitOfWork`, `IObjectStorage`,
  `IJobScheduler`, `ITurnStream`, `IIdGenerator`.
- `Turns.Domain` — сущности и правила хода; зависит только от `SharedKernel`.
- `Turns.Application` — сценарии, порты Turns, политика задач; зависит от `Turns.Domain`,
  `Events.Application`, `Events.Domain`, `SharedKernel`, `Application.Abstractions`.
- `Events.Domain` — событие, статусы, конфликты, правила поиска; зависит только от
  `SharedKernel`.
- `Events.Application` — создание, выборка, поиск, порты Events; зависит от `Events.Domain`,
  `SharedKernel`, `Application.Abstractions`.
- `TaskManager.Infrastructure` — EF Core, репозитории, миграции, Quartz, HTTP-клиенты, S3;
  зависит от Application/Domain модулей.
- `TaskManager.Api` — endpoint'ы, DI, конфигурация, SSE; зависит от Application и
  Infrastructure.

Никаких обратных зависимостей:

```
Api ─┐
     ├──► Turns.Application ──► Turns.Domain
Infra┘          │
                └──► Events.Application ──► Events.Domain
```

- `Events.*` не знает про `Turns.*`.
- `Turns.Domain` не знает про `Events.*`.
- `Turns.Application` может вызывать `Events.Application`.
- Domain не знает про транзакции EF Core: транзакцию открывает Application-сценарий.

---

## 4. Правила зависимостей и кода

1. **Domain без инфраструктуры.** Никаких EF-атрибутов, `DbContext`, `HttpClient`, Quartz,
   JSON-атрибутов контрактов и `ILogger` в Domain.
2. **Application без адаптеров.** Application объявляет порты; Infrastructure их реализует.
3. **Транзакция — в Application.** Один сценарий — одна транзакция; Domain принимает уже
   загруженные данные и возвращает изменения.
4. **Репозитории — по агрегатам, не generic.** `ITurnRepository`, `IDraftRepository`,
   `IEventRepository`; read-модели — через query-порты (`ITurnQuery`, `IEventQuery`).
5. **Ручной маппинг.** Между Domain, Application DTO и контрактами — явные методы; без
   AutoMapper.
6. **Типизированные ошибки.** Ожидаемые нарушения — `Result<T>` / `DomainError`; исключения —
   только для инвариантов и инфраструктурных сбоев.
7. **Идемпотентность.** Повторный шаг не создаёт дубль: CAS по `state_version`, уникальные
   ключи задач, `idempotency_key` хода.
8. **Время.** В Domain — `DateOnly` и `TimeOnly`; в БД — `date` и `time`; зона — `TIMEZONE`.
   `DateTimeOffset` — только для `timestamptz`.
9. **Стиль.** File-scoped namespaces, nullable enabled, `async`/`await` без `.Result`,
   `CancellationToken` во всех асинхронных методах, один публичный тип на файл.
10. **Логи.** Структурные логи с `turn_id`, `job_key`, `event_id`; без секретов и текстов
    пользователя в `Information`.

---

## 5. Domain

### 5.1 Turns.Domain

Сущности и правила:

- `Turn` — состояние, версия, дедлайн, текст, план, ошибка;
- `Draft` — незавершённый запрос, привязан к ходу, хранит `clarification_round`;
- `Answer` — текст ответа, `total_count`, `warnings`, аудио;
- `AnswerEvent` — связь ответа с событиями;
- `DomainCommand` — намерение записи, применяемое отдельной транзакцией.

Инварианты:

- переходы только по таблице состояний; терминальные состояния не меняются, кроме аудио у
  `COMPLETED`;
- CAS по `state_version`;
- один активный ход на сессию;
- `Draft` уникален по `turn_id`;
- не больше двух раундов уточнений;
- черновик сбрасывается на любом интенте, кроме `create_*` и `clarify`;
- `source_text` пополняется через `; `;
- `description` без `embedding` — `NLU_FAILED`, не Domain-ошибка.

### 5.2 Events.Domain

- `Event`: `Kind` (`task|reminder|meeting`), `Status` (`active|done|cancelled`),
  `Description` (обязательный заголовок), `SourceText`, `StartsOn`, `StartsAt`,
  `Duration`, `IsDeadline`.
- `Event.Create` проверяет:
  - заголовок обязателен для всех типов;
  - reminder и meeting требуют дату и время;
  - task может быть без даты и времени;
  - duration по умолчанию 60 минут для встречи;
  - `is_deadline` допустим только для task.
- `ConflictDetector`:
  - только `meeting`;
  - только `active`;
  - интервал `[start, start + duration)`, конец не входит;
  - возвращает все пересечения.
- `EventStatus` не переключается в MVP: `active` при создании, `done/cancelled` — LATER.
- Поиск и список:
  - `active + done`, `cancelled` исключён;
  - `type_filter`: `meeting` — встречи, `task` — задачи и напоминания, `reminder` —
    напоминания, `null` — все;
  - список: даты по `starts_on`, затем `starts_at`; записи без времени — в конце дня;
    задачи без даты — после датированных, блок «Без даты»;
  - поиск: семантика по `description`, top-3;
  - пустой поиск — пустой результат, не ошибка.

Domain не считает эмбеддинги. Вектор приходит готовым из `nlu`.

---

## 6. Application

### 6.1 Сценарии Turns

- `SubmitText` / `SubmitVoice` — создать ход, проверить идемпотентность и активный ход,
  поставить задачу.
- `HandleSttResult` — сохранить текст, перейти в `INTERPRETING`, поставить `nlu`.
- `InterpretTurn` — вызвать `nlu`, проверить слоты и эмбеддинг, выполнить чтение, запись или
  уточнение.
- `ApplyTurnPlan` — применить `domain_commands` одной транзакцией на ход.
- `SynthesizeAnswer` — поставить/выполнить `tts`, обновить аудио.
- `CancelDraft` — удалить черновик, вернуть текст подтверждения.
- `GetTurnSnapshot` / `StreamTurn` — отдать снапшот и SSE.
- `Reconcile` — закрыть просроченные ходы, добить задачи, пометить зависшую озвучку.

### 6.2 Сценарии Events

- `CreateEvent` — валидировать заголовок, слоты, тип, `is_deadline`, длительность.
- `ListPeriod` — вернуть до 3 записей + `total_count`, датированные и «Без даты».
- `Search` — семантический поиск top-3, фильтры `type_filter` и период.
- `DetectConflicts` — вернуть пересечения встреч для `warnings`.
- `UpdateEvent` — зарезервировано; в MVP не используется.

### 6.3 Транзакции

| Шаг | Что в одной транзакции |
|---|---|
| создание хода | `turns` + состояние |
| ответ `stt` | `turns.text`, `turns.state`, постановка `nlu` |
| `nlu`, чтение | `answers` + `answer_events` + `turns.state` |
| `nlu`, запись | `turns.plan` + `domain_commands` + `turns.state` |
| применение команд | `events` + `answers` + `answer_events` + `domain_commands` + `turns.state` |
| ответ `tts` | `answers.audio_*` |

Правило: данные и состояние меняются вместе. Постановка задачи в Quartz — **после** коммита;
если постановка не удалась, `reconciler` добьёт.

### 6.4 Ошибки

- Domain-ошибки (`TitleRequired`, `TimeInPast`, `InvalidPeriod`) превращаются в clarify или
  текст ответа.
- `description` без `embedding` → `FAILED` / `NLU_FAILED`; событие не создаётся, черновик не
  меняется.
- 4xx от `stt`/`nlu`/`tts` — permanent failure, без ретрая.
- 5xx, таймауты и сетевые ошибки — retry через Quartz.
- Ошибка `tts` не переводит ход в `FAILED`: только `audio_status = failed`.

---

## 7. Infrastructure и EF Core

### 7.1 DbContext

Один `TaskManagerDbContext` в `TaskManager.Infrastructure/Persistence`.

DbSet:

- `Turns`, `Drafts`, `Answers`, `AnswerEvents`, `DomainCommands`;
- `Events`.

Правила:

- конфигурации — отдельные `IEntityTypeConfiguration<T>` в `Persistence/Configurations`;
- миграции — только в `Persistence/Migrations`, одна история;
- никаких per-module контекстов и второго migration history;
- запросы чтения — `AsNoTracking` + проекции;
- запись — через репозитории и `IUnitOfWork`;
- `turns.state_version` — concurrency token; CAS-переходы через `ExecuteUpdate` с условием
  `state = expected and state_version = version`;
- `domain_commands` — выборка `FOR UPDATE SKIP LOCKED` через Npgsql/EF;
- `events.starts_at` — `time` без зоны, `DateOnly`/`TimeOnly` в Domain;
- `events.embedding` — `vector(1536)`; HNSW-индекс создаётся миграцией.

### 7.2 Репозитории и query-порты

- `ITurnRepository`, `IDraftRepository`, `IAnswerRepository`, `IEventRepository` — запись.
- `ITurnQuery`, `IEventQuery` — чтение снапшотов, списков и поиска.
- Никакого generic repository.
- Никаких SQL-строк в Application.
- Транзакция открывается в Application через `IUnitOfWork`; репозитории не коммитят.

### 7.3 Внешние адаптеры

- `SttClient`, `NluClient`, `TtsClient` — typed `HttpClient`, JSON по контрактам,
  таймауты из конфига.
- `ObjectStorage` — presigned PUT/GET, TTL; `content_type` определяется по расширению
  `object_key`. Неизвестное расширение — `application/octet-stream`. `HeadObject` не
  используется.
- `TurnStream` — in-memory hub подписок; одна реплика.
- `IJobScheduler` — обёртка над Quartz; Application не знает типов Quartz.

---

## 8. Quartz 4.x и фоновые задачи

### 8.1 Задачи

| Задача | Ключ | Таймаут | Попыток | Финал |
|---|---|---|---|---|
| `stt` | `stt:{turn_id}` | 30 с | 3 | `FAILED` / `STT_FAILED` |
| `nlu` | `nlu:{turn_id}` | 20 с | 3 | `FAILED` / `NLU_FAILED` |
| `tts` | `tts:{answer_id}` | 20 с | 3 | `audio_status = failed` |
| `apply` | singleton, 1 с | 5 с | — | следующий тик |
| `reconciler` | singleton, 10 с | 10 с | — | следующий тик |

### 8.2 Retry policy

- Использовать **встроенную retry policy Quartz 4.x**, не писать свой цикл.
- Повторять: сетевые ошибки, таймауты, `5xx`.
- Не повторять: `4xx`, ошибки валидации, невалидный контракт. Для них — permanent failure.
- Финальная обработка — `ITriggerListener.TriggerRetriesExhausted`: перевести ход в `FAILED`
  с нужным кодом, пометить `audio_status = failed` или завершить `DOMAIN_APPLY_FAILED`.
- Не использовать `Thread.Sleep`; задержка — перепланирование триггера.
- Ключ задачи детерминирован; повторная постановка не создаёт вторую задачу.

### 8.3 Идемпотентность

- Каждый job читает актуальное состояние и проверяет ожидаемое.
- Переход — CAS по `state_version`.
- Поздний результат после `EXPIRED`/`FAILED` отбрасывается.
- `apply` применяет все команды хода одной транзакцией; повтор не создаёт дублей.
- `reconciler` добивает отсутствующие задачи; постановка идемпотентна.

---

## 9. API и SSE

### 9.1 Endpoint'ы

| Метод | Путь | Ответ |
|---|---|---|
| `POST` | `/api/v1/uploads` | `201` + presigned PUT |
| `POST` | `/api/v1/messages` | `202` + `turn_id` |
| `GET` | `/api/v1/turns/{id}` | снапшот |
| `GET` | `/api/v1/turns/{id}/events` | SSE |
| `DELETE` | `/api/v1/turns/{id}/draft` | `200 {"text":"Хорошо, не сохраняю"}` |
| `GET` | `/api/v1/health` | health |

Правила:

- `POST /messages`: ровно одно из `text`/`object_key`; `idempotency_key`; один активный ход;
  `202` до обработки.
- `DELETE /draft`: идемпотентен; `404 TURN_NOT_FOUND`, если хода нет; голосовой отмены нет.
- Ошибки — единый формат `error.json`; коды не переименовывать.
- Новые поля ответа — только необязательные.

### 9.2 SSE

- `state` на каждый переход.
- `answer` с текстом, `events`, необязательными `total_count` и `warnings` — сразу, не
  дожидаясь `tts`.
- `audio` — после `answer`, если готово.
- Снапшот при подключении: `state`, затем `answer`, затем `audio`.
- `: ping` каждые 15 с.
- После `answer` соединение держится до 30 секунд для аудио, затем закрывается. Синтез
  продолжается в фоне; клиент может получить аудио через снапшот.
- `answer` и `error` взаимоисключающие.

---

## 10. Поведенческие правила

### 10.1 Статусы

- `active`, `done`, `cancelled` существуют.
- Переключение статусов — LATER; в MVP `done`/`cancelled` только из seed/ручной правки.
- Поиск и список: `active + done`, `cancelled` исключён.

### 10.2 Создание

- Заголовок (`description`) обязателен для task, reminder, meeting.
- Reminder: дата и время обязательны.
- Meeting: дата и время обязательны; duration по умолчанию 60 минут.
- Task: дата и время необязательны; `is_deadline` — только для task.
- `source_text` хранит исходные фразы; эмбеддинг — только по `description`.

### 10.3 Валидация и уточнения

- `starts_on`/`starts_at` принимать строками; невалидную дату/время не сохранять.
- Невалидный слот удалить, добавить в `missing`, задать общий вопрос:
  «Такой даты не существует. На какой день поставить?» /
  «Такого времени не существует. На какое время поставить?».
- Прошлое время: «Это время уже прошло. На какое время перенести?» с сохранением черновика.
- Максимум 2 раунда; после второго — `COMPLETED`, «Не удалось уточнить. Сформулируй запрос
  иначе.», черновик удалить.
- Один вопрос за раз; приоритет: заголовок → дата → время.

### 10.4 Черновик

- Слияние слотов; `source_text` через `; `.
- Сброс на любом интенте, кроме `create_*` и `clarify`.
- Отмена через `DELETE /turns/{id}/draft`.
- При `NLU_FAILED` черновик не создаётся и не меняется.

### 10.5 Поиск и список

- Лимит 3 для поиска и списка.
- Поиск: семантика, top-3, `active + done`, `cancelled` исключён.
- Список: `starts_on`, затем `starts_at`; без времени — в конце дня; задачи без даты — после
  датированных, блок «Без даты».
- `total_count` — только для списка, до лимита.
- `type_filter`: `meeting` / `task` (задачи + напоминания) / `reminder` / `null`.
- Период поиска — необязателен.
- Пустой поиск — `COMPLETED`, пустой `events`, «Ничего похожего не нашла».

### 10.6 Конфликты встреч

- Только `meeting`, только `active`.
- Интервал `[start, start + duration)`, конец не входит.
- Встреча сохраняется всегда.
- `warnings` содержит все пересечения; в тексте — первая; TTS читает текст с
  предупреждением.

### 10.7 NLU и TTS

- `nlu.request` не меняется; новые поля `nlu.response` (`type_filter`, `is_deadline`) —
  необязательные.
- Новые значения `action` не добавляются.
- `description` без `embedding` → `NLU_FAILED`.
- TTS синтезируется всегда в фоне; UI решает, играть ли.
- Ошибка TTS — только текст, без отдельной ошибки.

---

## 11. Данные

Одна база Postgres, расширение `pgvector`, один `DbContext`, одна история миграций.

Ключевые изменения относительно старой схемы:

- `events.description` — обязательный заголовок;
- `events.source_text` — исходные фразы, не участвует в эмбеддинге;
- `events.starts_at` — `time` без зоны, интерпретируется в `TIMEZONE`;
- `events.is_deadline` — только task;
- `events.embedding` — `vector(1536)`, HNSW-индекс; `NOT NULL` для событий с описанием.
  Модель — `text-embedding-3-small`;
- `drafts.source_text`, `drafts.clarification_round`, `drafts.is_deadline`;
- `answers.total_count`, `answers.warnings`;
- `domain_commands.payload` содержит `source_text` и `is_deadline`.

Ограничения:

- `kind in ('task','reminder','meeting')`;
- `status in ('active','done','cancelled')`;
- `starts_at` требует `starts_on`;
- `duration` требует `starts_at`;
- `duration > 0`;
- `is_deadline` только для task;
- `description` не пустой.

---

## 12. Конфигурация

| Переменная | Значение |
|---|---|
| `USER_ID` | `demo-user` |
| `SESSION_ID` | фиксированный uuid |
| `TIMEZONE` | `Europe/Moscow` |
| `DEADLINE_SECONDS` | `90` |
| `STT_TIMEOUT` / `NLU_TIMEOUT` / `TTS_TIMEOUT` | `30` / `20` / `20` |
| `JOB_MAX_ATTEMPTS` | `3` |
| `APPLY_TICK_SECONDS` | `1` |
| `RECONCILE_SECONDS` | `10` |
| `SSE_AUDIO_WAIT_SECONDS` | `30` |
| `AUDIO_RETENTION_DAYS` | `7` |
| `EMBEDDING_MODEL` | `text-embedding-3-small` |
| `EMBEDDING_DIM` | `1536` |
| `PRESIGN_TTL_SECONDS` | `600` / `900` |

`content_type` — по расширению `object_key`; неизвестное — `application/octet-stream`.

---

## 13. Тестирование

### 13.1 Стратегия

- **Unit-тесты — только Domain.** `Turns.Domain.Tests`, `Events.Domain.Tests`.
- **Интеграционные тесты — всё остальное.** Application, Infrastructure, Api, SSE, Quartz,
  EF-миграции.
- Тесты пишем в семантике Arrange, Act, Assert с явными комментариями
  `// Arrange`, `// Act`, `// Assert`.
- Тесты проверяют наблюдаемое поведение, а не детали реализации.
- В unit-тестах Domain нет out-of-process collaborators, поэтому моки не нужны.
  Исключения — только если появится реальная внешняя зависимость.
- Утверждения — FluentAssertions (`.Should()`), без xUnit `Assert`.
- БД — реальный Postgres через Testcontainers.
- Внешние зависимости — `stt`, `nlu`, `tts`, S3 — NSubstitute.
- Планировщик в тестах Application — fake `IJobScheduler`; Quartz-адаптер проверяется
  отдельным интеграционным тестом с in-memory store.
- Семантический поиск проверяется на реальном Postgres/pgvector (`vector(1536)`).

### 13.2 Обязательные кейсы

- создание task/reminder/meeting; обязательные слоты;
- невалидная дата/время → clarify, черновик сохранён;
- прошлое время → clarify;
- два раунда уточнений → завершение и удаление черновика;
- отмена черновика и идемпотентность;
- поиск: top-3, `active + done`, `type_filter`, пустой результат;
- список: сортировка, «Без даты», `total_count`;
- конфликты встреч: `[start, end)`, warnings, текст, TTS;
- `description` без `embedding` → `NLU_FAILED`;
- Quartz retry: 5xx/timeout повторяются, 4xx — нет, `TriggerRetriesExhausted` завершает;
- SSE: текст сразу, аудио позже, 30 секунд, снапшот;
- второй ход при активном → `409 SESSION_BUSY`;
- старый NLU без `type_filter`/`is_deadline` работает.

---

## 14. Порядок реализации

1. Solution, проекты, `Directory.Build.props`, central package management.
2. `SharedKernel`, Domain Turns/Events, unit-тесты Domain.
3. Application-порты и сценарии Turns/Events.
4. Infrastructure: `DbContext`, конфигурации, репозитории, миграции с `events.embedding vector(1536)` и HNSW.
5. Quartz 4.x: jobs, retry policy, listener, scheduler adapter.
6. Api: endpoint'ы, SSE, health, DI, конфигурация.
7. Интеграционные тесты и Testcontainers.
8. Search в `Events.Application` и тесты семантического поиска на реальном Postgres/pgvector.

Definition of done:

- `dotnet build src/TaskManager/TaskManager.sln` и
  `dotnet test src/TaskManager/TaskManager.sln` зелёные;
- `python docs/contracts/validate.py` зелёный;
- старый NLU принимается новым API;
- нет новых обязательных полей в `nlu.request`;
- нет новых значений `action`;
- документация и схемы обновлены вместе с кодом.

---

## 15. TBD и блокеры

- `EMBEDDING_MODEL` и `EMBEDDING_DIM` — зафиксированы: `text-embedding-3-small`, `1536`.
  Колонка `vector(1536)` и HNSW-индекс создаются миграцией.
- `content_type` — решено: расширение `object_key`, без `HeadObject`.
- `total_count` — решено: необязательное поле списка.
- Переключение статусов, удаление, отметка выполнения — LATER.
- Брокер, outbox и мультипользовательность — LATER, см. `../for_investigation.md`.
