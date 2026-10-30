from __future__ import annotations

import json

from .config import settings
from .embeddings import embed
from .llm import call_llm
from .prompt import SYSTEM_PROMPT, build_user_prompt
from .rules import interpret_rules
from .schemas import InterpretRequest, InterpretResponse


def interpret(req: InterpretRequest) -> InterpretResponse:
    draft_dict = req.draft.model_dump(exclude_none=True) if req.draft else None

    # 1) rules-based
    rules_result = interpret_rules(req.text, req.now, draft_dict)

    # 2) LLM (если не RULES_ONLY)
    if settings.rules_only or not settings.llm_api_key:
        plan = rules_result
        provider, model = "rules", "rules-v1"
    else:
        try:
            llm_result = call_llm(
                SYSTEM_PROMPT,
                build_user_prompt(req.text, req.now, req.timezone, draft_dict),
            )
            # sanity: если LLM вернул мусор — падаем на rules
            if not isinstance(llm_result, dict) or "action" not in llm_result:
                raise ValueError("LLM вернул не то")
            plan = llm_result
            provider, model = "llm", settings.llm_model
        except Exception:
            plan = rules_result
            provider, model = "rules_fallback", "rules-v1"

    action = plan["action"]
    entities = plan.get("entities") or None
    missing = plan.get("missing")
    question = plan.get("question")
    confidence = float(plan.get("confidence", 0.7))

    # 3) эмбеддинг
    embedding = None
    if action in ("create_task", "create_reminder", "create_meeting"):
        if entities and entities.get("description"):
            embedding = embed(entities["description"])
        else:
            embedding = None
    elif action == "search":
        if entities and entities.get("query"):
            embedding = embed(entities["query"])
        else:
            embedding = None
    elif action == "clarify":
        # если уточнение по описанию — эмбеддим уже известное описание
        if draft_dict and draft_dict.get("description"):
            embedding = embed(draft_dict["description"])
        else:
            embedding = None
    else:
        embedding = None

    # 4) нормализация: если create_meeting без времени → clarify
    if action == "create_meeting" and entities:
        if not entities.get("starts_at"):
            action = "clarify"
            missing = ["time"]
            question = "На какое время поставить встречу?"
        elif not entities.get("starts_on"):
            action = "clarify"
            missing = ["datetime"]
            question = "На какой день и время?"

    # 5) проставляем duration_minutes по умолчанию
    if action == "create_meeting" and entities and not entities.get("duration_minutes"):
        entities["duration_minutes"] = 60

    return InterpretResponse(
        turn_id=req.turn_id,
        action=action,
        confidence=confidence,
        entities=entities,
        missing=missing,
        question=question,
        embedding=embedding,
        provider=provider,
        model=model,
    )
