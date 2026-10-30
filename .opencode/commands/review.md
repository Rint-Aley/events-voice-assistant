---
description: Review the current diff with a read-only reviewer
agent: reviewer
subagent: true
---

Проверь текущие изменения в бэкенде TaskManager.

Status:

!`git status --short`

Stat:

!`git diff HEAD --stat`

Diff:

!`git diff HEAD`

Сначала оцени размер diff. Если он больше ~700 строк или затронуто больше 2–3 проектов,
предложи разбить задачу. Если в `git status` есть untracked files, прочитай их напрямую:
они могли не попасть в diff.

Проверь:

1. Контракты и совместимость со старым NLU.
2. Границы Domain / Application / Infrastructure / Api.
3. Поведение: clarify, cancel, statuses, list/search, conflicts, retries.
4. Тесты: unit только Domain, интеграционные с Testcontainers; Arrange/Act/Assert
   с комментариями, FluentAssertions, в unit-тестах Domain без моков.
5. Миграции: один DbContext, один migration history, без `vector(N)`/HNSW до TBD.
6. Ошибки: коды и формат `error.json`.

Ничего не меняй. Верни только findings:

- `blockers` — что нельзя коммитить;
- `should-fix` — ошибки и пропущенные edge cases;
- `nit` — стиль и мелочи.

Для каждого finding: severity, `file:line`, что не так, как проверить.
Если блокеров нет — напиши `blockers: none`.
