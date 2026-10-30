# Промпты для написания бэкенда TaskManager

Готовые промпты для OpenCode. Каждый промпт рассчитан на одну сессию и один коммит;
коммит делает человек, агент только пишет код. Вставляй промпт целиком, не добавляй к нему
старый контекст из предыдущих задач.

Все пути к бэкенду указаны от корня репозитория: `src/TaskManager/...`. В `src/` рядом
лежат `tts`, `stt`, `nlu` и `web`, поэтому проекты бэкенда не создаются в корне `src/`.

Тесты — xUnit + NSubstitute + FluentAssertions. Пиши их в семантике Arrange, Act, Assert
с комментариями `// Arrange`, `// Act`, `// Assert`; проверяй наблюдаемое поведение, а не
детали реализации. В unit-тестах Domain моки не нужны, кроме исключительных случаев.

## Как пользоваться

1. Переключись на агент **Build**.
2. Создай ветку под задачу:
   ```bash
   git switch -c task/01-scaffold
   ```
3. Открой новую сессию и вставь промпт.
4. После задачи проверь `dotnet build src/TaskManager/TaskManager.sln`,
   `dotnet test src/TaskManager/TaskManager.sln`, `git diff --check`.
5. Перед review выполни `git add -A` без коммита, чтобы `git diff HEAD` включал новые файлы.
6. Запусти `/review` в новой сессии.
7. Исправь findings, закоммить.
8. Следующая задача — новая сессия.

Порядок задач:

| # | Задача | Зависит от |
|---|---|---|
| 00 | Проверка окружения | — |
| 01 | Scaffold | — |
| 02 | SharedKernel + Abstractions | 01 |
| 03 | Events.Domain | 02 |
| 04 | Turns.Domain | 02 |
| 05 | Persistence | 03, 04 |
| 06 | Events.Application | 05 |
| 07 | Turns intake | 06 |
| 08 | Turns apply + answer | 07 |
| 09 | Infrastructure adapters | 02 |
| 10 | Quartz | 08 |
| 11 | API + SSE | 09, 10 |
| 12 | E2E integration tests | 11 |
| 13 | Vector search | TBD по embedding |
| 14 | Ручной review | после задачи |
| 15 | Исправление findings | после review |
| 16 | Handoff в следующую сессию | в конце задачи |

`03` и `04` можно делать параллельно. `09` и `10` тоже можно параллельно после `08`.
`13` нельзя начинать до фиксации `EMBEDDING_MODEL` и `EMBEDDING_DIM`.

---

## 00. Проверка окружения

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md и docs/task-manager/implementation.md. Ничего не меняй.

Проверь окружение:
- git status;
- dotnet --info;
- доступен ли Docker (для Testcontainers);
- есть ли уже `src/TaskManager/TaskManager.sln`, `src/TaskManager/` и `src/TaskManager/tests`;
- какие .NET SDK установлены.

Верни:
1. статус репозитория;
2. версию .NET;
3. доступен ли Docker;
4. какие файлы уже есть;
5. что можно начинать, а что заблокировано.

Не создавай и не меняй файлы. Не коммить.
```

---

## 01. Scaffold

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §2–4 и docs/architecture.md §8.
Сначала кратко перечисли файлы, которые создашь. Если нужно добавить пакет, которого нет
в списке ниже, остановись и спроси. Не коммить.

Создай скелет solution в `src/TaskManager/`:

- src/TaskManager/TaskManager.sln;
- src/TaskManager/TaskManager.SharedKernel;
- src/TaskManager/TaskManager.Application.Abstractions;
- src/TaskManager/Modules/Turns/Turns.Domain;
- src/TaskManager/Modules/Turns/Turns.Application;
- src/TaskManager/Modules/Events/Events.Domain;
- src/TaskManager/Modules/Events/Events.Application;
- src/TaskManager/TaskManager.Infrastructure;
- src/TaskManager/TaskManager.Api;
- src/TaskManager/tests/Modules/Turns/Turns.Domain.Tests;
- src/TaskManager/tests/Modules/Events/Events.Domain.Tests;
- src/TaskManager/tests/TaskManager.IntegrationTests.

Добавь project references строго по правилам из AGENTS.md:
- Domain зависит только от SharedKernel;
- Turns.Application зависит от Turns.Domain, Events.Application, Events.Domain,
  SharedKernel, Application.Abstractions;
- Events.Application зависит от Events.Domain, SharedKernel, Application.Abstractions;
- Infrastructure зависит от Application/Domain модулей;
- Api зависит от Application и Infrastructure;
- тестовые проекты ссылаются только на то, что тестируют.

Добавь:
- `src/TaskManager/Directory.Build.props`: nullable, implicit usings, LangVersion;
- `src/TaskManager/Directory.Packages.props` с централизованными версиями пакетов,
  если используешь central package management;
- `global.json` в корне репозитория с зафиксированным .NET SDK;
- `.editorconfig` в корне репозитория;
- `.gitignore` для .NET, если его нет.

Пакеты, которые разрешены на этом шаге:
- Npgsql.EntityFrameworkCore.PostgreSQL;
- Pgvector и Pgvector.EntityFrameworkCore;
- Microsoft.EntityFrameworkCore.Design для `dotnet ef`;
- Quartz 4.x;
- xUnit;
- NSubstitute;
- FluentAssertions;
- Testcontainers.PostgreSql;
- Microsoft.AspNetCore.Mvc.Testing для интеграционных тестов.

Бизнес-код не добавляй. Пустые тестовые проекты допустимы.

Проверка:
- dotnet build src/TaskManager/TaskManager.sln;
- dotnet test src/TaskManager/TaskManager.sln;
- git diff --check.

В конце отчитайся: список файлов, версии пакетов, результаты команд, что осталось.
```

---

## 02. SharedKernel + Application.Abstractions

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md и docs/task-manager/implementation.md §3–4.
Сначала кратко перечисли файлы. Не добавляй пакеты. Не коммить.

Реализуй TaskManager.SharedKernel:

- Result и Result<T> с признаком успеха, ошибкой и значением;
- DomainError/Error: код, сообщение, признак ожидаемой ошибки;
- IClock с текущим UTC и локальным временем по TIMEZONE;
- IIdGenerator;
- минимальные общие value objects, если они действительно нужны Domain.

Реализуй TaskManager.Application.Abstractions:

- IUnitOfWork: начало транзакции, commit, rollback, SaveChanges;
- IObjectStorage: создание presigned PUT и GET;
- IJobScheduler: schedule, exists, remove;
- ITurnStream: publish/subscribe для SSE;
- IIdGenerator, если он не попал в SharedKernel.

Требования:
- без реализаций инфраструктуры;
- без EF Core, ASP.NET, Quartz и HttpClient;
- интерфейсы минимальные, без методов «на будущее»;
- все асинхронные методы принимают CancellationToken.

Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, решения по интерфейсам, результаты команд.
```

---

## 03. Events.Domain

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §5.2,
docs/task-manager/task-manager.md §7.4 и §7.8, docs/contracts/nlu.response.json
в части type_filter и is_deadline.

Реализуй Events.Domain и unit-тесты. Не коммить. Не добавляй пакеты.

Домен:
- Event, EventKind, EventStatus;
- Description — обязательный заголовок для task, reminder и meeting;
- SourceText — исходные фразы пользователя;
- StartsOn (DateOnly?), StartsAt (TimeOnly?), Duration, IsDeadline;
- task может быть без даты и времени;
- reminder и meeting требуют дату и время;
- duration встречи по умолчанию 60 минут;
- is_deadline допустим только для task;
- статусы active, done, cancelled; создание всегда active;
- переходы статусов в MVP не реализуй.

ConflictDetector:
- только meeting;
- только active;
- интервал [start, start + duration), конец не входит;
- возвращает все пересечения;
- встреча сохраняется всегда, конфликт — это warning, не ошибка.

Правила выборки:
- type_filter: meeting — встречи; task — задачи и напоминания;
  reminder — только напоминания; null — все;
- поиск и список: active + done, cancelled исключён;
- список: starts_on, затем starts_at; без времени — в конце дня;
- задачи без даты — после датированных, отдельный блок «Без даты».

Unit-тесты обязательны:
- обязательный заголовок;
- обязательные слоты для reminder/meeting;
- duration по умолчанию;
- is_deadline только task;
- конфликты и границы интервала;
- type_filter;
- сортировка и «Без даты».

Запрещено: EF Core, JSON-атрибуты контрактов, HTTP, Quartz, ILogger.
Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, тесты, результаты команд.
```

---

## 04. Turns.Domain

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §5.1,
docs/task-manager/task-manager.md §4, §5, §7.5.

Реализуй Turns.Domain и unit-тесты. Не коммить. Не добавляй пакеты.

Домен:
- Turn, TurnState, state_version;
- состояния: TRANSCRIBING, INTERPRETING, APPLYING, COMPLETED,
  NEEDS_CLARIFICATION, FAILED, EXPIRED;
- допустимые переходы — строго по таблице §4.2;
- терминальные состояния не меняются, кроме аудио у COMPLETED;
- CAS по state_version;
- один активный ход на сессию;
- Draft, уникальный по turn_id;
- Draft.SourceText, ClarificationRound;
- не больше двух раундов уточнений;
- сброс черновика на любом интенте, кроме create_* и clarify;
- Answer, AnswerEvent, DomainCommand;
- отмена черновика идемпотентна;
- source_text пополняется через `; `.

Ожидаемые ошибки — через Result/DomainError:
- второй активный ход;
- недопустимый переход;
- превышен лимит уточнений;
- отмена уже удалённого черновика не является ошибкой.

Unit-тесты обязательны:
- каждый переход из каждого состояния;
- CAS и параллельный переход;
- лимит уточнений;
- сброс черновика;
- отмена и идемпотентность;
- запрет второго активного хода.

Запрещено: EF Core, HTTP, SSE, Quartz, ILogger.
Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, тесты, результаты команд.
```

---

## 05. Persistence

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §7,
docs/task-manager/task-manager.md §7.

Реализуй слой хранения. Не коммить. Не добавляй пакеты без согласования.

Нужно:
- один TaskManagerDbContext в src/TaskManager/TaskManager.Infrastructure/Persistence;
- конфигурации IEntityTypeConfiguration для turns, drafts, answers,
  answer_events, domain_commands, events;
- репозитории по агрегатам: ITurnRepository, IDraftRepository,
  IAnswerRepository, IEventRepository;
- реализация IUnitOfWork на транзакции DbContext;
- query-порты ITurnQuery и IEventQuery там, где они нужны Application;
- миграции в одном migration history в
  src/TaskManager/TaskManager.Infrastructure/Persistence/Migrations.

Схема должна учитывать:
- events.description not null;
- events.source_text;
- events.starts_on date;
- events.starts_at time без зоны;
- events.duration interval, только для встреч;
- events.is_deadline boolean, только для task;
- events.embedding vector без размерности;
- HNSW-индекс не создавать;
- drafts.source_text, drafts.clarification_round, drafts.is_deadline;
- answers.total_count, answers.warnings;
- turns.state_version для CAS;
- domain_commands.payload с source_text и is_deadline.

Требования:
- без generic repository;
- без второго DbContext;
- без SQL в Application;
- без AutoMapper;
- миграция должна применяться к реальному Postgres.

Добавь один интеграционный тест: миграции применяются к Postgres через Testcontainers.
Если Docker недоступен — остановись и сообщи, не подменяй Postgres на in-memory.

Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, имя миграции, результат применения, тесты.
```

---

## 06. Events.Application

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §6.2,
docs/task-manager/task-manager.md §7.8, docs/contracts/http-api.md.

Реализуй Events.Application. Не коммить. Не добавляй пакеты.

Нужно:
- CreateEvent: валидация заголовка, слотов, типа, is_deadline, duration;
- ListPeriod: до 3 записей, total_count, active + done, cancelled исключён;
- сортировка: starts_on, затем starts_at; без времени — в конце дня;
- задачи без даты — после датированных, блок «Без даты»;
- DetectConflicts для warnings;
- репозиторий IEventRepository и query-порт IEventQuery;
- DTO/команды и ручной маппинг, без AutoMapper.

Search:
- если EMBEDDING_MODEL и EMBEDDING_DIM уже зафиксированы — реализуй семантический
  поиск top-3 с фильтрами type_filter и периодом;
- если нет — не реализуй Search и не создавай фейковую заглушку. Сообщи, что задача
  заблокирована TBD.

Интеграционные тесты с реальным Postgres:
- создание task/reminder/meeting;
- обязательные слоты;
- лимит 3 и total_count;
- «Без даты»;
- type_filter;
- конфликты встреч.

Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, тесты, что заблокировано.
```

---

## 07. Turns intake

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §6.1, §6.3, §6.4,
docs/task-manager/task-manager.md §4, §5.2–5.5, §7.5, §9–10.

Реализуй Turns.Application, часть «приём запроса». Не коммить. Не добавляй пакеты.

Сценарии:
- SubmitText: idempotency_key, проверка активного хода, создание хода;
- SubmitVoice: создание хода и постановка STT;
- HandleSttResult: сохранение текста, переход в INTERPRETING, постановка NLU;
- InterpretTurn: вызов NLU через порт, разбор action/entities/missing;
- CancelDraft: удаление черновика, идемпотентность, текст
  «Хорошо, не сохраняю»;
- GetTurnSnapshot: состояние, ответ, события, total_count, warnings, аудио.

Нормализация entities:
- starts_on и starts_at принимать строками, не DateOnly/TimeOnly;
- невалидную дату/время не сохранять в черновик;
- удалять невалидный слот, добавлять date/time в missing, задавать общий вопрос;
- прошлое время: clarify с сохранением черновика;
- description без embedding — NLU_FAILED, событие не создаётся, черновик не меняется;
- type_filter и is_deadline необязательны: старый NLU их не присылает;
- черновик сбрасывается на любом интенте, кроме create_* и clarify;
- clarification_round, максимум 2 раунда;
- после второго раунда — COMPLETED, текст
  «Не удалось уточнить. Сформулируй запрос иначе.», черновик удалить.

Порты: ISttClient, INluClient, ITurnRepository, IDraftRepository,
IAnswerRepository. Реализации не нужны, в тестах — fakes.

Тесты:
- fake STT/NLU, реальный Postgres;
- text/voice intake;
- clarify и два раунда;
- невалидная дата/время;
- прошлое время;
- NLU_FAILED;
- cancel идемпотентен;
- второй активный ход.

Не реализуй apply, TTS и SSE. Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, тесты, результаты, блокеры.
```

---

## 08. Turns apply + answer

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §6.1–6.4,
docs/task-manager/task-manager.md §5.4, §7.6, §7.8,
docs/assistant_replies.md.

Реализуй Turns.Application, часть «применение и ответ». Не коммить.
Не добавляй пакеты.

Нужно:
- ApplyTurnPlan: блокировка хода, выборка pending domain_commands по seq,
  применение одной транзакцией на ход;
- вызов Events.Application для создания событий;
- DetectConflicts и заполнение warnings;
- answers, answer_events, текст ответа по docs/assistant_replies.md;
- TTS-порт и постановка задачи, без реализации HTTP-клиента;
- total_count для списка;
- идемпотентность: повторное применение не создаёт дублей;
- CAS и переходы APPLYING → COMPLETED/FAILED;
- ошибка применения — DOMAIN_APPLY_FAILED.

Текст ответа:
- брать только из docs/assistant_replies.md;
- женский род, «ты», без восклицаний и извинений;
- предупреждение о конфликте встреч — в тексте, первая встреча;
- TTS читает текст с предупреждением.

Тесты:
- создание события и ответ;
- warnings при конфликте;
- повторное применение без дублей;
- total_count;
- ошибка применения;
- TTS-задача поставлена, но не выполнена.

Не реализуй SSE и HTTP. Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, тесты, результаты.
```

---

## 09. Infrastructure adapters

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §7.3,
docs/contracts/stt.*.json, nlu.*.json, tts.*.json, error.json,
docs/infra.md §4.

Реализуй адаптеры. Не коммить. Не добавляй пакеты без согласования.

Нужно:
- SttClient, NluClient, TtsClient на typed HttpClient;
- JSON по контрактам, таймауты из конфига;
- 4xx — permanent failure, 5xx/timeout/network — retryable;
- ObjectStorage: presigned PUT/GET, TTL;
- content_type по расширению object_key;
- неизвестное расширение — application/octet-stream;
- HeadObject не использовать;
- TurnStream: in-memory hub подписок для SSE.

Тесты:
- fake HttpMessageHandler для клиентов;
- проверка 4xx/5xx/таймаута;
- content_type по расширению;
- подписка/публикация TurnStream.

Не реализуй Quartz, API и бизнес-логику.
Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, тесты, результаты.
```

---

## 10. Quartz

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §8,
docs/task-manager/task-manager.md §5.

Реализуй Quartz 4.x. Не коммить. Не добавляй пакеты без согласования.

Нужно:
- persistent store в Postgres;
- jobs: stt, nlu, tts, apply, reconciler;
- детерминированные ключи: stt:{turn_id}, nlu:{turn_id}, tts:{answer_id};
- встроенная retry policy Quartz 4.x, без своего цикла и Thread.Sleep;
- 5xx/timeout/network — retry, 4xx — permanent failure;
- ITriggerListener.TriggerRetriesExhausted: финальная обработка;
- stt/nlu → FAILED с кодом;
- tts → audio_status = failed;
- apply → DOMAIN_APPLY_FAILED;
- reconciler: просроченные ходы, отсутствующие задачи, зависшая озвучка;
- IJobScheduler — обёртка, Application не знает типов Quartz.

Тесты:
- 4xx не повторяется;
- 5xx повторяется;
- exhausted вызывает финальный обработчик;
- повторная постановка не создаёт вторую задачу;
- reconciler добивает отсутствующую задачу.

Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, тесты, результаты.
```

---

## 11. API + SSE

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §9,
docs/task-manager/task-manager.md §8–10,
docs/contracts/http-api.md, docs/contracts/error.json.

Реализуй TaskManager.Api. Не коммить. Не добавляй пакеты без согласования.

Endpoint'ы:
- POST /api/v1/uploads;
- POST /api/v1/messages;
- GET /api/v1/turns/{id};
- GET /api/v1/turns/{id}/events;
- DELETE /api/v1/turns/{id}/draft;
- GET /api/v1/health.

Требования:
- DI, конфигурация, единый формат ошибок;
- 202 на messages до обработки;
- ровно одно из text/object_key;
- idempotency_key;
- 409 SESSION_BUSY при активном ходе;
- отмена черновика идемпотентна, 404 TURN_NOT_FOUND;
- SSE: snapshot, state, answer сразу, audio позже;
- ping каждые 15 секунд;
- соединение до 30 секунд после answer;
- answer и error взаимоисключающие;
- старый NLU без type_filter/is_deadline работает.

Тесты:
- WebApplicationFactory + Testcontainers;
- fake STT/NLU/TTS/S3/Quartz;
- все endpoint'ы и коды;
- SSE snapshot и порядок answer/audio;
- отмена черновика;
- ошибки.

Бизнес-логику не дублируй в контроллерах.
Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: файлы, тесты, результаты.
```

---

## 12. E2E integration tests

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager (C#/ASP.NET Core).
Бэкенд создаётся в `src/TaskManager/`; рядом в `src/` будут `tts`, `stt`, `nlu` и `web`.
Не создавай проекты бэкенда прямо в `src/`.

Прочитай AGENTS.md, docs/task-manager/implementation.md §13.2,
docs/task-manager/task-manager.md §12, docs/contracts/http-api.md.

Добавь интеграционные E2E-тесты. Не коммить. Не добавляй пакеты.

Сценарии:
- создание task/reminder/meeting;
- clarify и два раунда;
- отмена черновика и идемпотентность;
- список, total_count, «Без даты»;
- type_filter;
- конфликты встреч и warnings;
- description без embedding → NLU_FAILED;
- SSE: текст сразу, аудио позже;
- 409 SESSION_BUSY;
- старый NLU без type_filter/is_deadline;
- 4xx/5xx и retry через Quartz.

Условия:
- реальный Postgres через Testcontainers;
- STT/NLU/TTS/S3/Quartz — fakes;
- не подменяй Postgres на in-memory;
- если тест находит баг — исправляй минимально, не расширяй задачу;
- семантический поиск с vector(N) не тестируй до TBD.

Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: список тестов, найденные баги, результаты.
```

---

## 13. Vector search — только после TBD

```text
Не начинай эту задачу, пока не зафиксированы EMBEDDING_MODEL и EMBEDDING_DIM.
Если они всё ещё TBD(BLOCKING) — остановись и сообщи об этом.

После фиксации:
- добавь миграцию events.embedding vector(N);
- добавь HNSW-индекс;
- реализуй Search в Events.Application: top-3, active + done, cancelled исключён;
- поддержи type_filter и необязательный период;
- пустой результат: COMPLETED, пустой events, «Ничего похожего не нашла»;
- добавь тесты семантического поиска на реальном Postgres/pgvector.

Не меняй контракты. Не коммить.
Проверка: dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.
В конце отчитайся: модель, размерность, миграция, тесты.
```

---

## 14. Ручной review

```text
Ты — read-only reviewer бэкенда TaskManager. Ничего не редактируй и не коммить.

Проверь текущий diff:
- git diff HEAD --stat;
- git diff HEAD.

Сначала оцени размер: если больше ~700 строк или затронуто больше 2–3 проектов,
предложи разбить задачу.

Проверь:
1. Контракты: nlu.request не изменён; новые поля nlu.response необязательные;
   новые action не добавлены; старый NLU поддерживается.
2. Границы: Domain без EF Core/ASP.NET/Quartz/HttpClient; Events не зависит от Turns;
   Turns.Application может зависеть от Events; транзакции только в Application.
3. Поведение: обязательный заголовок; clarify максимум 2 раунда; отмена черновика;
   лимит 3; «Без даты»; type_filter; конфликты встреч; NLU_FAILED; retry 4xx/5xx.
4. Тесты: unit только Domain; интеграционные с Testcontainers; edge cases.
5. Миграции: один DbContext, один migration history, без vector(N)/HNSW до TBD.
6. Ошибки: коды не переименованы, формат error.json не изменён.

Верни только findings:
- severity: blocker / should-fix / nit;
- file:line;
- что не так;
- как проверить.

Если блокеров нет — напиши «blockers: none».
```

---

## 15. Исправление findings

```text
Ты работаешь в D:\Projects\todo-agent над бэкендом TaskManager.

Исправь только перечисленные findings. Не расширяй задачу.
Не коммить.

Findings:
{{вставить вывод review}}

Правила:
- не меняй контракты без разрешения;
- не добавляй пакеты;
- не трогай несвязанные файлы;
- если finding требует архитектурного решения — остановись и спроси;
- после исправления запусти dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check.

В конце отчитайся:
1. какие findings исправлены;
2. какие нет и почему;
3. результаты команд;
4. итоговый diff --stat.
```

---

## 16. Handoff в следующую сессию

```text
Ты заканчиваешь задачу в D:\Projects\todo-agent. Бэкенд лежит в `src/TaskManager/`.
Не меняй код.

Собери короткий handoff:
1. что сделано;
2. какие файлы изменены;
3. какие тесты добавлены;
4. результаты dotnet build src/TaskManager/TaskManager.sln, dotnet test src/TaskManager/TaskManager.sln, git diff --check;
5. что не сделано;
6. какие блокеры и TBD остались;
7. какой промпт из docs/task-manager/prompts.md запускать следующим.

Формат — короткий Markdown, без воды.
```
