from __future__ import annotations

import hashlib
import math

import httpx

from .config import settings
from .schemas import Embedding


def _deterministic_stub(text: str, dim: int) -> list[float]:
    """Детерминированный псевдо-вектор для RULES_ONLY и тестов."""
    h = hashlib.sha256(text.encode("utf-8")).digest()
    raw = [(b / 255.0) * 2 - 1 for b in (h * ((dim // len(h)) + 1))[:dim]]
    norm = math.sqrt(sum(x * x for x in raw)) or 1.0
    return [x / norm for x in raw]


def embed(text: str) -> Embedding:
    if settings.rules_only or not settings.embedding_api_key:
        return Embedding(
            model="stub-sha256",
            dim=settings.embedding_dim,
            values=_deterministic_stub(text, settings.embedding_dim),
        )

    resp = httpx.post(
        f"{settings.embedding_base_url}/embeddings",
        headers={"Authorization": f"Bearer {settings.embedding_api_key}"},
        json={"model": settings.embedding_model, "input": text},
        timeout=settings.llm_timeout,
    )
    resp.raise_for_status()
    data = resp.json()["data"][0]["embedding"]
    return Embedding(model=settings.embedding_model, dim=len(data), values=data)
