# TaskManager

Версия: 1. Владелец: бэкенд. Основано на `docs/architecture.md` v0.9.

Спека описывает внутреннее устройство TaskManager: модули, машину состояний, фоновые задачи,
взаимодействие с `stt`/`nlu`/`tts` и схему данных. JSON-схемы в этот файл не входят: здесь
только смысл полей, точные контракты — в `docs/contracts/`.

---

## 1. Ответственность и границы

**Делает:**

- принимает аудио и текст от `web`, ведёт ход обработки, отдаёт состояние и события по SSE;
- хранит единственный источник правды о записях: задачи, напоминания, встречи;
- вызывает `stt`, `nlu`, `tts` по HTTP и решает, что делать с их ответами;
- хранит эмбеддинги записей и выполняет поиск по смыслу через pgvector;
- следит за дедлайнами, ретраями и восстановлением после падения.

**Не делает:**

- не распознаёт речь, не разбирает текст и не синтезирует аудио — это чужие сервисы;
- не хранит историю диалога: она живёт на клиенте, сервер отдаёт только конкретный ход;
- не планирует напоминания и не отправляет уведомления (MVP только сохраняет время);
- не занимается авторизацией: один пользователь на весь MVP.

MVP-допущения, зашитые в конфиг:

| Что | Значение |
|---|---|
| `user_id` | константа, в выборках не используется |
| `session_id` | константа; все ходы принадлежат одной сессии |
| `timezone` | константа, передаётся в `nlu` и используется для относительных дат |

Мультипользовательность и сессии добавляются без изменения схемы `turns`: появляется таблица
`sessions`, а `turns.session_id` получает FK и реальные значения.

---

## 2. Модули и адаптеры

Один процесс, модульный монолит. Доменов два, вокруг них — три адаптера. Планировщик
адаптером не считается: это внутренняя библиотека `fsm` (§5).

### 2.1 Модули

| Модуль | Ответственность | Таблицы |
|---|---|---|
| `fsm` | состояния хода, переходы, транзакции, сборка ответа, политика фоновых задач | `turns`, `drafts`, `answers`, `answer_events`, `domain_commands` |
| `domain` | операции над `events`, валидация, поиск, pgvector | `events` |

Внутри `fsm`:

```
fsm
  ├─ turns    ход, состояния, транзакции
  ├─ jobs     какие задачи ставим, ретраи, дедлайны, тики
  └─ ports    TurnStream, SttClient, NluClient, TtsClient, ObjectStorage
```

`jobs` — не отдельный модуль и не адаптер, а часть application-слоя: политика фоновых задач.
Библиотека планировщика живёт там же и пишет только в свои таблицы.

### 2.2 Адаптеры

| Адаптер | Направление | Ответственность | Реализует порт |
|---|---|---|---|
| `http` | вход + поток | маршруты, валидация входа, коды ответов, SSE-фрейминг, хаб подписок | `TurnStream` |
| `clients` | выход | HTTP-вызовы `stt`/`nlu`/`tts` | `SttClient`, `NluClient`, `TtsClient` |
| `storage` | выход | presigned URL и ключи S3 | `ObjectStorage` |

Хаб подписок — внутренняя часть `http`; отдельного модуля `sse` нет. `fsm` публикует события
только через порт `TurnStream`.

### 2.3 Зависимости

Стрелка — импорт:

```
http ────┐
clients ─┼──► fsm ──► domain
storage ─┘
```

В рантайме вызовы идут в обе стороны через порты: `http → fsm.submit_text()`,
`fsm → http.publish()`. Импортов из `fsm` в адаптеры нет: порт объявляет тот, кому он нужен.

Правила:

- `domain` не импортирует `fsm`, не знает про HTTP, SSE, адаптеры и состояния ходов;
- `fsm` не импортирует конкретные адаптеры и не пишет в `events` напрямую;
- только `fsm` открывает и коммитит транзакции, `domain` принимает уже открытую;
- адаптеры не содержат доменных правил, не пишут SQL и не открывают транзакции;
- транспорт тонкий: вся логика перехода — в `fsm`;
- домен возвращает типизированные ошибки (`TitleRequired`, `InvalidPeriod`,
  `TimeInPast`), `fsm` превращает их в ответ пользователю.

---

## 3. Модель исполнения

### 3.1 Ход

Ход (`turns`) — один цикл «запрос → ответ». У него ровно один ответ, свой дедлайн и свои
задачи. Ход живёт секунды, сессия (в MVP одна на всё) — сколько угодно.

```
POST /messages
   │
   ├─ текст  ──────────────► INTERPRETING ──► job nlu:{turn_id}
   └─ голос  ──► TRANSCRIBING ──► job stt:{turn_id}
                        │
                        └─ ответ stt ──► INTERPRETING ──► job nlu:{turn_id}

nlu:
   ├─ чтение  (выборка, поиск)        ──► COMPLETED  (+ answers)
   ├─ запись  (задача, встреча)       ──► APPLYING   (+ domain_commands)
   └─ уточнение                       ──► NEEDS_CLARIFICATION (+ drafts)

APPLYING ──► тик применяет команды ──► COMPLETED (+ events, answers)
COMPLETED ──► job tts:{answer_id} ──► audio дописывается, состояние не меняется
```

### 3.2 Границы транзакций

| Шаг | Что в одной транзакции |
|---|---|
| создание хода | `turns` + состояние |
| ответ `stt` | `turns.text`, `turns.state`, постановка `nlu` |
| `nlu`, чтение | `answers` + `answer_events` + `turns.state` |
| `nlu`, запись | `turns.plan` + `domain_commands` + `turns.state` |
| применение команд | `events` + `answers` + `answer_events` + `domain_commands` + `turns.state` |
| ответ `tts` | `answers.audio_*` |

Правило: **данные и состояние меняются вместе**. Если процесс упал до коммита — не
применилось ничего, шаг повторяется. Если после — шаг уже завершён.

### 3.3 Постановка задач

Ключ задачи детерминирован:

| Задача | Ключ |
|---|---|
| распознавание | `stt:{turn_id}` |
| разбор | `nlu:{turn_id}` |
| синтез | `tts:{answer_id}` |

Планировщик держит не больше одной задачи на ключ, поэтому повторная постановка безопасна:
она либо создаёт отсутствующую задачу, либо ничего не делает. Именно это закрывает разрыв
между коммитом и `schedule` — планировщик не участвует в транзакции БД.

### 3.4 Дедлайн и восстановление

- `deadline_at = now() + 90 с` при создании хода.
- `reconciler` (раз в 10 с) закрывает просроченные ходы в `EXPIRED` и добивает недостающие
  задачи (см. §5.6).
- Падение процесса посреди задачи: планировщик возвращает её в работу
  (`RequestRecovery`), обработчик идемпотентен за счёт CAS по `state_version`.
- Одна реплика: SSE-шина в памяти процесса. При нескольких репликах понадобится общая шина.

---

## 4. Машина состояний

### 4.1 Состояния

| Состояние | Смысл | Терминальное |
|---|---|---|
| `TRANSCRIBING` | ждём `stt` | нет |
| `INTERPRETING` | ждём `nlu` | нет |
| `APPLYING` | ждём применения `domain_commands` | нет |
| `COMPLETED` | ответ готов; `tts` может дописать аудио | да |
| `NEEDS_CLARIFICATION` | ход закончился вопросом, черновик сохранён | да |
| `FAILED` | обработка не удалась | да |
| `EXPIRED` | дедлайн истёк | да |

Состояний «выполняю домен» и «отправляю ответ» нет: они живут внутри транзакции и наружу не
видны, поэтому не хранятся.

### 4.2 Переходы

| Из | Событие | В | Действие |
|---|---|---|---|
| — | `POST /messages`, голос | `TRANSCRIBING` | создать ход, поставить `stt` |
| — | `POST /messages`, текст | `INTERPRETING` | создать ход, поставить `nlu` |
| `TRANSCRIBING` | ответ `stt` | `INTERPRETING` | сохранить текст, поставить `nlu` |
| `TRANSCRIBING` | тишина в записи | `FAILED` | `EMPTY_TRANSCRIPT` |
| `TRANSCRIBING` | попытки исчерпаны | `FAILED` | `STT_FAILED` |
| `INTERPRETING` | план: чтение | `COMPLETED` | `answers` + `answer_events`, поставить `tts` |
| `INTERPRETING` | план: чтение, домен вернул ошибку валидации | `NEEDS_CLARIFICATION` | вопрос по слоту |
| `INTERPRETING` | план: запись | `APPLYING` | `plan` + `domain_commands` |
| `INTERPRETING` | план: уточнение | `NEEDS_CLARIFICATION` | `drafts` + ответ-вопрос |
| `INTERPRETING` | план: не понял | `COMPLETED` | ответ без доменного действия |
| `INTERPRETING` | попытки исчерпаны | `FAILED` | `NLU_FAILED` |
| `APPLYING` | команды применены | `COMPLETED` | `events` + `answers`, поставить `tts` |
| `APPLYING` | попытки исчерпаны | `FAILED` | `DOMAIN_APPLY_FAILED` |
| `COMPLETED` | ответ `tts` | `COMPLETED` | `audio_status = ready` |
| любое нетерминальное | `deadline_at` прошёл | `EXPIRED` | `DEADLINE_EXCEEDED` |

### 4.3 Инварианты

1. **CAS на каждом переходе.** Никаких `update ... set state = ...` без ожидаемого состояния:

   ```sql
   update turns
      set state = :new, state_version = state_version + 1, updated_at = now()
    where id = :id and state = :expected and state_version = :version
   returning *;
   ```

   Ноль строк — переход уже сделан или устарел; результат отбрасывается.

2. **Один активный ход.** Частичный уникальный индекс:

   ```sql
   create unique index turns_one_active
       on turns (session_id)
       where state in ('TRANSCRIBING', 'INTERPRETING', 'APPLYING');
   ```

   Второе сообщение до завершения первого получает `409 SESSION_BUSY`.

3. **Терминальные состояния не меняются.** Исключение — `COMPLETED` + `tts`: меняются
   только поля аудио у `answers`, состояние хода остаётся прежним.

4. **Поздний результат отбрасывается.** Ответ `stt`/`nlu`, пришедший после `EXPIRED` или
   `FAILED`, не пишется: CAS не сработает.

5. **Черновик принадлежит ходу.** `drafts.turn_id` уникален; после успешного применения
   черновик удаляется, а ход остаётся `NEEDS_CLARIFICATION` — это честная история.

---

## 5. Фоновые задачи

Планировщик — библиотека с персистентным стором в том же Postgres (Quartz.NET или аналог).
Своей таблицы задач TaskManager не держит. Это внутренняя деталь `fsm`, а не адаптер:
планировщик обслуживает собственный процесс, пишет только в свои таблицы, а его типы не
выходят за пределы `fsm/jobs`. Вызовы библиотеки спрятаны за внутренним интерфейсом
`JobQueue` (`schedule`, `exists`, `remove`) — он нужен для тестов и архитектурным портом не
является.

### 5.1 Задачи

| Тип | Ключ | Payload | Таймаут | Попыток | Если исчерпаны |
|---|---|---|---|---|---|
| `stt` | `stt:{turn_id}` | `turn_id` | 30 с | 3 | ход → `FAILED`, `STT_FAILED` |
| `nlu` | `nlu:{turn_id}` | `turn_id` | 20 с | 3 | ход → `FAILED`, `NLU_FAILED` |
| `tts` | `tts:{answer_id}` | `answer_id` | 20 с | 3 | `audio_status = failed` |
| `apply` | singleton, 1 с | — | 5 с | — | следующий тик |
| `reconciler` | singleton, 10 с | — | 10 с | — | следующий тик |

Ретраи — не `sleep` внутри задачи, а перепланирование того же ключа с задержкой (1 с, 4 с) и
номером попытки в данных задачи. Повторяются сетевые ошибки, таймауты и `5xx`; `4xx` — сразу
окончательный отказ.

### 5.2 Обработчик `stt`

1. Прочитать ход, проверить `state = TRANSCRIBING`.
2. Сгенерировать presigned GET на `audio_object_key` (TTL 15 минут, не хранить).
3. `POST` в `stt`: ссылка + параметры.
4. Транзакция: `turns.text`, `stt_confidence`, `state = INTERPRETING`, поставить `nlu`.

Пустой текст (`empty = true`) — не ошибка транспорта: ход сразу `FAILED` /
`EMPTY_TRANSCRIPT`, без ретраев.

### 5.3 Обработчик `nlu`

1. Прочитать ход и активный черновик (последний ход в `NEEDS_CLARIFICATION`).
2. `POST` в `nlu`: текст, текущий момент, таймзона, черновик.
3. Разобрать план по действию:
   - **чтение** — выполнить запрос домена, собрать ответ и `answer_events`, транзакция:
     `answers` + `turns.state = COMPLETED`, поставить `tts`;
   - **запись** — транзакция: `plan`, `domain_commands` (по одной на запись, `seq` по порядку),
     `state = APPLYING`; сразу после коммита попробовать применить (тик — страховка);
   - **уточнение** — транзакция: `drafts` (upsert по `turn_id`), `answers` с вопросом,
     `state = NEEDS_CLARIFICATION`;
   - **не понял** — `answers` с просьбой переформулировать, `state = COMPLETED`.

Если описание есть, эмбеддинг обязателен: описание без вектора — ошибка контракта, ход
уходит в `FAILED` / `NLU_FAILED`. Если описания нет вовсе, нет и вектора — запись создаётся,
но в семантическом поиске не участвует.

### 5.4 Применение команд

Тик `apply`:

```sql
select id, turn_id
  from domain_commands
 where status = 'pending' and next_attempt_at <= now()
 order by next_attempt_at
 limit 50
   for update skip locked;
```

Строки группируются по `turn_id`, каждый ход применяется отдельной транзакцией:

1. `select ... from turns where id = :turn_id for update` — проверить `state = APPLYING`;
2. `select` команды хода со `status = 'pending'` по возрастанию `seq` (под блокировкой);
3. применить каждую через `domain` (создание записи, позже — обновление);
4. вставить `answers` и `answer_events` (порядок — как в плане);
5. `domain_commands.status = 'applied'`, `applied_at = now()`;
6. `turns.state = COMPLETED`;
7. после коммита — поставить `tts:{answer_id}`.

Почему одной транзакцией на ход, а не задачей на команду: порядок `seq` соблюдается сам,
«создай задачу и встречу» применяется целиком или никак, ретрай не создаёт дублей.

Ошибка применения: `attempts += 1`, `last_error`, `next_attempt_at = now() + backoff`. После
3 попыток — `status = 'failed'`, ход → `FAILED` / `DOMAIN_APPLY_FAILED`.

### 5.5 Обработчик `tts`

1. Прочитать `answers` по `answer_id`, проверить `audio_status = 'pending'`.
2. `POST` в `tts`: текст, голос, формат.
3. Транзакция: `audio_object_key`, `audio_status = 'ready'`, `audio_duration_ms`.

Провал не влияет на ход: текст уже отдан. После исчерпания попыток —
`audio_status = 'failed'`, пользователь видит текст без озвучки.

### 5.6 `reconciler`

Раз в 10 секунд:

1. Ходы в `TRANSCRIBING`/`INTERPRETING` с `deadline_at < now()` → `EXPIRED`.
2. Ходы в `TRANSCRIBING`/`INTERPRETING`, для которых нет задачи с ожидаемым ключом → поставить
   задачу заново (постановка идемпотентна).
3. `answers` с `audio_status = 'pending'` старше 60 с и без задачи → `failed`.
4. Ход в `APPLYING`, у которого все команды в `failed` → `FAILED` / `DOMAIN_APPLY_FAILED`
   (страховка на случай, если обработчик тика не успел это сделать).

---

## 6. Взаимодействие с сервисами

Общие правила: только TaskManager вызывает сервисы; сервисы не общаются между собой и не
имеют доступа к БД; блобы не передаются — только ссылки S3 и текст; вызовы идут из
обработчиков задач, пользовательское соединение они не держат.

### 6.1 `stt`

Отправляем:

| Поле | Смысл |
|---|---|
| `turn_id` | для логов и корреляции |
| `audio_url` | presigned GET, TTL 15 минут |
| `language` | язык распознавания (константа MVP) |

Получаем: `text`, `confidence`, `empty`, `duration_ms`, `provider`.

Пишем: `turns.text`, `turns.stt_confidence`. Аудио остаётся в S3, в БД только ключ.

### 6.2 `nlu`

Отправляем:

| Поле | Смысл |
|---|---|
| `turn_id` | корреляция |
| `text` | распознанный или введённый текст |
| `now` | текущий момент с таймзоной (для «завтра», «в пятницу») |
| `timezone` | IANA-зона пользователя |
| `draft` | незавершённые слоты из `drafts` или `null` |

Получаем: `action` (создать задачу / создать напоминание / создать встречу / выборка за
период / поиск / уточнение / не понял), `entities` (описание, дата, время,
продолжительность, период, поисковый запрос), `confidence`, `embedding`, `missing`
(чего не хватает для уточнения), `question` (текст уточняющего вопроса).

Пишем: `turns.plan` (сырой план — для отладки), `domain_commands.payload` (нормализованные
поля + эмбеддинг) либо `drafts` (недостающие слоты).

Правило: `nlu` не знает про БД и не решает, что записывать. Он отдаёт план, TaskManager
переводит его в команды.

### 6.3 `tts`

Отправляем: `answer_id`, текст ответа, голос, формат.

Получаем: `object_key`, `duration_ms`, `cached`.

Пишем: `answers.audio_object_key`, `audio_duration_ms`, `audio_status`. Ключ объекта
детерминирован по тексту и голосу, поэтому повторный вызов безопасен.

---

## 7. Данные

Одна база Postgres, расширение `pgvector`. Все временные метки — `timestamptz`.

### 7.1 Связи

```
turns 1 ──── 1 answers ──── * answer_events * ──── 1 events
  │                                                     ▲
  ├──── 1 drafts                              source_turn_id
  └──── * domain_commands
```

### 7.2 `turns`

```sql
create table turns (
    id               uuid primary key default gen_random_uuid(),
    session_id       uuid not null,            -- константа из конфига
    state            text not null,
    state_version    int  not null default 1,
    idempotency_key  uuid not null unique,
    text             text,
    audio_object_key text,
    stt_confidence   numeric(4,3),
    plan             jsonb,
    error_code       text,
    deadline_at      timestamptz not null,
    created_at       timestamptz not null default now(),
    updated_at       timestamptz not null default now(),

    constraint turns_state_check check (state in (
        'TRANSCRIBING', 'INTERPRETING', 'APPLYING',
        'COMPLETED', 'NEEDS_CLARIFICATION', 'FAILED', 'EXPIRED'))
);

create unique index turns_one_active on turns (session_id)
    where state in ('TRANSCRIBING', 'INTERPRETING', 'APPLYING');
create index turns_deadline on turns (state, deadline_at);
create index turns_recent on turns (created_at desc);
```

- `idempotency_key` — от клиента; повтор `POST /messages` возвращает существующий ход.
- `text` — введённый текст или результат распознавания.
- `plan` — последний ответ `nlu` как есть.

### 7.3 `domain_commands`

```sql
create table domain_commands (
    id              uuid primary key default gen_random_uuid(),
    turn_id         uuid not null references turns(id) on delete cascade,
    seq             smallint not null,
    op              text not null,
    payload         jsonb not null,
    status          text not null default 'pending',
    attempts        int  not null default 0,
    next_attempt_at timestamptz not null default now(),
    last_error      text,
    created_at      timestamptz not null default now(),
    applied_at      timestamptz,

    constraint domain_commands_op_check check (op in ('event.create', 'event.update')),
    constraint domain_commands_status_check check (status in ('pending', 'applied', 'failed')),
    constraint domain_commands_seq_unique unique (turn_id, seq)
);

create index domain_commands_pending on domain_commands (status, next_attempt_at);
```

`payload` для `event.create`: `kind`, `description`, `embedding`, `starts_on`, `starts_at`,
`duration`. Поля нормализует `fsm` из плана `nlu`.

### 7.4 `events`

```sql
create table events (
    id              uuid primary key default gen_random_uuid(),
    user_id         text not null,              -- константа, сейчас не используется
    kind            text not null,
    description     text,
    embedding       vector(1024),               -- размерность = модель эмбеддингов
    embedding_model text,
    starts_on       date,
    starts_at       timetz,
    duration        interval,
    status          text not null default 'active',
    source_turn_id  uuid references turns(id),
    created_at      timestamptz not null default now(),
    updated_at      timestamptz not null default now(),

    constraint events_kind_check check (kind in ('task', 'reminder', 'meeting')),
    constraint events_status_check check (status in ('active', 'done', 'cancelled')),
    constraint events_description_embedding_pair
        check ((description is null) = (embedding is null)),
    constraint events_model_pair
        check ((embedding is null) = (embedding_model is null)),
    constraint events_time_requires_date
        check (starts_at is null or starts_on is not null),
    constraint events_duration_requires_time
        check (duration is null or starts_at is not null),
    constraint events_duration_positive
        check (duration is null or duration > interval '0')
);

create index events_by_date on events (starts_on, starts_at);
create index events_by_status on events (status, starts_on);
create index events_by_source_turn on events (source_turn_id);
create index events_embedding_hnsw on events using hnsw (embedding vector_cosine_ops);
```

Правила:

- `description` и `embedding` живут парой: нет описания — нет и вектора. Запись без вектора
  не участвует в семантическом поиске, но находится по дате и типу.
- `starts_on` / `starts_at` необязательны: «позвонить маме» — задача без срока.
- `starts_at` — `time with time zone`: смещение хранится вместе со временем. Для MVP считаем,
  что пользователь в одной зоне; смещение берётся на дату события.
- `duration` — только для встреч и только вместе со временем.
- `status` создаётся в `active`; переходы в `done`/`cancelled` — LATER.

### 7.5 `drafts`

```sql
create table drafts (
    id          uuid primary key default gen_random_uuid(),
    turn_id     uuid not null unique references turns(id) on delete cascade,
    kind        text,
    description text,
    embedding   vector(1024),
    starts_on   date,
    starts_at   timetz,
    duration    interval,
    created_at  timestamptz not null default now(),
    updated_at  timestamptz not null default now(),

    constraint drafts_description_embedding_pair
        check ((description is null) = (embedding is null))
);
```

Черновик — недособранный запрос: система не создала запись, потому что не хватает слотов.
Он привязан к ходу в `NEEDS_CLARIFICATION`, а не к сессии. После успешного создания записи
удаляется; если пользователь ушёл в другую тему — тоже удаляется (старый ход остаётся
`NEEDS_CLARIFICATION` с пустой ссылкой).

### 7.6 `answers`

```sql
create table answers (
    id                uuid primary key default gen_random_uuid(),
    turn_id           uuid not null unique references turns(id) on delete cascade,
    text              text not null,
    audio_object_key  text,
    audio_status      text not null default 'skipped',
    audio_duration_ms int,
    created_at        timestamptz not null default now(),

    constraint answers_audio_status_check
        check (audio_status in ('pending', 'ready', 'failed', 'skipped'))
);
```

`audio_status`: `pending` — задача поставлена, `ready` — ключ есть, `failed` — озвучка не
удалась, `skipped` — озвучка не требовалась.

### 7.7 `answer_events`

```sql
create table answer_events (
    answer_id uuid not null references answers(id) on delete cascade,
    event_id  uuid not null references events(id) on delete cascade,
    position  smallint not null default 0,
    primary key (answer_id, event_id)
);
```

Нужна для выборки за период и поиска: ответ ссылается на список записей, `position` держит
порядок (для поиска — по убыванию близости).

### 7.8 Эмбеддинги и поиск

- Вектор приходит от `nlu` для всех действий и сохраняется как есть, без пересчёта.
- Поиск: `order by embedding <=> :query_vector` с фильтром по `status = 'active'` и лимитом.
- Порог отсечения и гибридный поиск (вектор + полнотекст) — LATER; сейчас top-N.
- Смена модели = смена размерности = новая колонка и переиндексация; поэтому
  `embedding_model` хранится рядом с вектором.

---

## 8. SSE

`GET /api/v1/turns/{turn_id}/events` — `text/event-stream`.

События:

| `event` | `data` |
|---|---|
| `state` | `{"state": "INTERPRETING"}` |
| `answer` | `{"text": "...", "events": [...]}` |
| `audio` | `{"url": "...", "duration_ms": 6100}` |
| `error` | `{"code": "STT_FAILED", "message": "..."}` |

Правила:

- При подключении сервер сразу отдаёт снапшот: текущий `state`, затем `answer`, если он уже
  есть, затем `audio`, если готово. Дальше — живые события. Это заменяет `Last-Event-ID`:
  журнал событий не хранится, а снапшот всегда актуален.
- `answer` и `error` взаимоисключающие, приходят один раз.
- `audio` приходит после `answer` и может не прийти — это не ошибка.
- Терминальный ход: снапшот и закрытие соединения.
- `: ping` каждые 15 с.
- Шина подписок — в памяти процесса (одна реплика). Клиент после разрыва переподключается и
  получает снапшот; дубли клиент отбрасывает по типу события.

Polling-fallback: `GET /api/v1/turns/{turn_id}` возвращает то же состояние.

---

## 9. HTTP-маршруты

Полные тела и коды — в `docs/contracts/http-api.md`. Здесь — только роль:

| Маршрут | Роль |
|---|---|
| `POST /api/v1/uploads` | presigned PUT для аудио |
| `POST /api/v1/messages` | создать ход, ответить `202` |
| `GET /api/v1/turns/{id}` | снапшот хода |
| `GET /api/v1/turns/{id}/events` | SSE |
| `GET /api/v1/health` | живость, БД, планировщик |

`POST /messages`:

1. `idempotency_key` уже известен → вернуть существующий ход и `202`.
2. Есть активный ход → `409 SESSION_BUSY`.
3. Создать ход одной транзакцией, затем поставить задачу; если постановка не удалась — её
   добьёт `reconciler`.
4. Ответ `202 {turn_id, state, events_url}`, где `state` — `TRANSCRIBING` для голоса и
   `INTERPRETING` для текста.

---

## 10. Ошибки

HTTP-ошибки (до создания хода): `BAD_REQUEST`, `UNSUPPORTED_AUDIO`, `PAYLOAD_TOO_LARGE`,
`SESSION_BUSY`, `TURN_NOT_FOUND`, `INTERNAL`.

Ошибки обработки — событием `error` внутри хода:

| Код | Причина | Что делает ход |
|---|---|---|
| `EMPTY_TRANSCRIPT` | в записи тишина | `FAILED` |
| `STT_FAILED` | попытки исчерпаны | `FAILED` |
| `NLU_FAILED` | попытки исчерпаны или описание пришло без вектора | `FAILED` |
| `DOMAIN_APPLY_FAILED` | команды не применились | `FAILED` |
| `DEADLINE_EXCEEDED` | 90 с истекли | `EXPIRED` |

`tts` не порождает ошибку хода: максимум `audio_status = failed`.

---

## 11. Конфигурация

| Переменная | Смысл | Значение MVP |
|---|---|---|
| `USER_ID` | константа пользователя | `demo-user` |
| `SESSION_ID` | константа сессии | фиксированный uuid |
| `TIMEZONE` | зона для относительных дат | `Europe/Moscow` |
| `DEADLINE_SECONDS` | потолок хода | `90` |
| `STT_TIMEOUT` / `NLU_TIMEOUT` / `TTS_TIMEOUT` | таймауты HTTP | `30` / `20` / `20` |
| `JOB_MAX_ATTEMPTS` | попыток на задачу | `3` |
| `APPLY_TICK_SECONDS` | период применения команд | `1` |
| `RECONCILE_SECONDS` | период сверки | `10` |
| `AUDIO_RETENTION_DAYS` | срок жизни аудио в S3 | `7` |
| `EMBEDDING_DIM` | размерность вектора | по модели `nlu` |
| `PRESIGN_TTL_SECONDS` | TTL ссылок S3 | `600` / `900` |

---

## 12. Как проверить

Юнит-тесты:

- таблица переходов: каждое событие из каждого состояния — разрешено или нет;
- CAS: параллельные переходы, второй проигрывает;
- черновик: мерж слотов, удаление после успеха, «другая тема»;
- дедлайн: ход в `TRANSCRIBING` + просроченный `deadline_at` → `EXPIRED`;
- раскладка плана `nlu` в `domain_commands` (порядок `seq`, обязательные слоты).

Интеграционные (заглушки `stt`/`nlu`/`tts`):

1. Текстовый запрос «создай задачу…» → `INTERPRETING` → `APPLYING` → `COMPLETED`,
   запись в `events`, ответ ссылается на неё.
2. Голосовой запрос → `TRANSCRIBING` → `INTERPRETING` → `COMPLETED`, аудио ответа приходит
   отдельным событием.
3. «Что у меня на неделе» → `COMPLETED`, `answer_events` содержит список.
4. «Найди про встречу с Иваном» → поиск по вектору, порядок по близости.
5. Уточнение: «создай встречу» → `NEEDS_CLARIFICATION` + черновик; следующий ход с ответом
   создаёт запись и удаляет черновик.
6. Падение между коммитом и постановкой задачи → `reconciler` ставит задачу, ход доходит до
   `COMPLETED`.
7. Падение посреди `nlu` → задача повторяется, дубля записи нет.
8. `stt` отвечает `500` трижды → `FAILED` / `STT_FAILED`.
9. `tts` отвечает `500` трижды → `COMPLETED`, `audio_status = failed`.
10. Второе сообщение при активном ходе → `409 SESSION_BUSY`.

Ручной сценарий — четыре обязательных кейса из ТЗ: создание задачи/напоминания, создание
встречи, выборка за период, поиск; плюс относительные даты («завтра», «в пятницу») и
сохранение данных между запросами.

---

## 13. Что отложено

- доставка напоминаний (MVP только сохраняет время);
- редактирование, удаление, отметка выполнения;
- конфликты встреч и повторяющиеся события;
- мультипользовательность: таблица `sessions`, реальные `session_id`/`user_id`;
- журнал SSE-событий и честный replay по `Last-Event-ID`;
- гибридный поиск и пороги отсечения;
- вынос `domain` в отдельный сервис (тогда `domain_commands` станет настоящим outbox);
- переход на брокер — `docs/for_investigation.md` §4.
