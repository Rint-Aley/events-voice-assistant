# Спецификация: карта документов

Обновлено: 2026-10-30. Это точка входа в документацию: что где лежит, в каком порядке
читать и куда идти с конкретным вопросом. Решения здесь не дублируются — только ссылки.

## Что за проект

Голосовой ассистент для задач и встреч: пользователь диктует или пишет запрос, система
распознаёт речь, разбирает смысл, создаёт задачу / напоминание / встречу, отвечает текстом
и аудио, умеет выбирать записи за период и искать по смыслу.

Источник требований — ТЗ в корне репозитория
(`Testovoe_zadanie_golosovoj_assistent_dlya_zadach_i_261021_115649.pdf`): четыре
обязательных сценария. Работа ведётся по SDD: сначала контракты и спеки, потом код.

## Порядок чтения

1. `architecture.md` — состав системы, потоки, обмен, данные. Начинать отсюда.
2. `task-manager/task-manager.md` — главный сервис: состояния, фоновые задачи, таблицы, SSE.
3. `task-manager/implementation.md` — стек, проекты, слои, Quartz, правила реализации.
4. `contracts/README.md` — HTTP-контракты; JSON-схемы лежат рядом.
5. `infra.md` — Postgres и S3 локально, presigned URL.
6. `for_investigation.md` — что отложено и почему. Это не договорённость, а повестка.

## Файлы

| Файл | Версия | О чём | Когда открывать |
|---|---|---|---|
| `architecture.md` | 0.10 | компоненты, потоки, обмен с сервисами, фоновая обработка, данные, границы модулей и адаптеров | нужна картина целиком или меняется взаимодействие |
| `task-manager/task-manager.md` | 1.1 | FSM, фоновые задачи, транзакции, DDL, SSE, маршруты, ошибки, конфиг | реализуешь или меняешь бэкенд |
| `task-manager/implementation.md` | 1 | стек, solution, проекты, зависимости, EF Core, Quartz 4.x, тесты | начинаешь писать бэкенд |
| `task-manager/prompts.md` | 1 | готовые промпты по задачам бэкенда | запускаешь агента на очередную задачу |
| `assistant_replies.md` | 1 | авторитетные тексты ответов ассистента | пишешь ответы, TTS или UI-копирайт |
| `infra.md` | 1.3 | Postgres + pgvector, S3/RustFS, presigned, compose | поднимаешь окружение |
| `contracts/README.md` | набор 2.1 | правила HTTP-обмена, таймауты, ретраи, владельцы схем | добавляешь или меняешь вызов между сервисами |
| `contracts/http-api.md` | 1.1 | web ↔ TaskManager: upload, messages, turns, draft cancel, SSE | делаешь фронтенд или API |
| `contracts/*.json` | 2.1 | схемы `stt`/`nlu`/`tts` (request/response) и `error.json` | нужен точный формат тела |
| `contracts/validate.py` | — | самопроверка схем: позитивные и негативные кейсы | правил контракты |
| `contracts/archive/` | 1 | брокерные схемы `*.job.json` / `*.result.json` | планируешь переход на брокер |
| `for_investigation.md` | — | LATER, НФТ, сценарий брокера, открытые вопросы | ищешь «почему так» или что ещё не решено |
| `deploy/README.md` | — | запуск Postgres и S3, проверка, сброс | поднимаешь зависимости |

Спеки `stt.md`, `nlu.md`, `tts.md`, `web.md` ещё не написаны: их шаблон — в
`architecture.md` §9.

## Где что искать

| Вопрос | Куда |
|---|---|
| Кто кого вызывает, что за деплой-юниты | `architecture.md` §2–3 |
| Модули и адаптеры внутри TaskManager | `task-manager/task-manager.md` §2, `architecture.md` §8 |
| Структура solution, проекты и зависимости | `task-manager/implementation.md` §3–4 |
| EF Core, DbContext, миграции | `task-manager/implementation.md` §7 |
| Quartz 4.x, ретраи, финальные ошибки | `task-manager/implementation.md` §8 |
| Тестовая стратегия и матрица | `task-manager/implementation.md` §13 |
| Промпты для агента | `task-manager/prompts.md` |
| Тексты ответов и формулировки | `assistant_replies.md` |
| Голосовой и текстовый запрос по шагам | `architecture.md` §4.1–4.2 |
| Уточняющий диалог и черновик | `architecture.md` §4.3, `task-manager/task-manager.md` §7.5 |
| Эмбеддинги и поиск по смыслу | `architecture.md` §4.4, `task-manager/task-manager.md` §7.8 |
| Что именно ждём от `stt`/`nlu`/`tts` | `architecture.md` §5, `task-manager/task-manager.md` §6 |
| Таймауты, ретраи, идемпотентность | `architecture.md` §6, `contracts/README.md` |
| Состояния хода и разрешённые переходы | `task-manager/task-manager.md` §4 |
| Дедлайн, восстановление, `reconciler` | `task-manager/task-manager.md` §3.4, §5.6 |
| Применение `domain_commands` | `task-manager/task-manager.md` §5.4 |
| Таблицы, ограничения, индексы (SQL) | `task-manager/task-manager.md` §7 |
| SSE: события, снапшот, реконнект | `task-manager/task-manager.md` §8, `contracts/http-api.md` §5 |
| Отмена черновика | `contracts/http-api.md` §4, `task-manager/task-manager.md` §9 |
| HTTP-маршруты и коды ответов | `contracts/http-api.md`, `task-manager/task-manager.md` §9–10 |
| Ошибки сервисов и `retryable` | `contracts/error.json`, `contracts/README.md` |
| Переменные окружения | `task-manager/task-manager.md` §11, `infra.md` §5, `deploy/.env.example` |
| Что отложено | `for_investigation.md` §3, `task-manager/task-manager.md` §13, `infra.md` §7 |
| История изменений архитектуры | `architecture.md` §10 |

## Рамки MVP

Зафиксировано в спеке, менять — только осознанно:

- один пользователь и **одна сессия**: `user_id` и `session_id` — константы в конфиге,
  авторизации нет;
- брокера и outbox нет: HTTP-вызовы из фоновых задач, планировщик + `domain_commands`;
- напоминания не доставляются — MVP только сохраняет время;
- истории диалога на сервере нет, она живёт на клиенте; сервер отдаёт конкретный ход;
- таймзоны: одна зона на пользователя, переходы на летнее время игнорируются;
- `description` — обязательный заголовок; если описание есть, `embedding` обязателен,
  иначе ход получает `NLU_FAILED`;
- статусы `active`/`done`/`cancelled` есть, но переключение статусов — LATER; поиск и
  список показывают `active + done`, `cancelled` исключают.

## Правила работы с документами

1. `architecture.md` — только договорённое. Недоговорённое — в `for_investigation.md`.
2. Один факт живёт в одном месте: FSM и таблицы — в `task-manager/task-manager.md`, правила
   реализации — в `task-manager/implementation.md`, форматы — в `contracts/`, инфраструктура —
   в `infra.md`. Остальные документы ссылаются.
3. Поменял формат — правь схему и прогоняй `docs/contracts/validate.py`. Расхождение кода
   и схемы — баг.
4. Поменял архитектуру — обнови версию в шапке `architecture.md` и добавь строку в §10.
5. Ссылки — относительные пути от `docs/`. Не пересказывай чужие разделы: ставь ссылку.

## Проверка

```bash
python docs/contracts/validate.py           # действующие схемы
python docs/contracts/archive/validate.py   # архив брокерных схем
docker compose -f deploy/docker-compose.yml config --quiet
```

Нужен `jsonschema` и `referencing`: `pip install jsonschema referencing`.
