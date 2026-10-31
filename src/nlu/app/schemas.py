from __future__ import annotations

from enum import Enum
from typing import Literal, Optional

from pydantic import BaseModel, Field, field_validator, model_validator


class Kind(str, Enum):
    task = "task"
    reminder = "reminder"
    meeting = "meeting"


class Action(str, Enum):
    create_task = "create_task"
    create_reminder = "create_reminder"
    create_meeting = "create_meeting"
    list_period = "list_period"
    search = "search"
    clarify = "clarify"
    unknown = "unknown"


class Draft(BaseModel):
    kind: Optional[Kind] = None
    description: Optional[str] = Field(default=None, max_length=500)
    starts_on: Optional[str] = None
    starts_at: Optional[str] = None
    duration_minutes: Optional[int] = Field(default=None, ge=5, le=1440)


class InterpretRequest(BaseModel):
    turn_id: str
    text: str = Field(min_length=1, max_length=1000)
    now: str
    timezone: str = Field(min_length=1, max_length=64)
    draft: Optional[Draft] = None


class Entities(BaseModel):
    description: Optional[str] = Field(default=None, max_length=500)
    starts_on: Optional[str] = None
    starts_at: Optional[str] = None
    duration_minutes: Optional[int] = Field(default=None, ge=5, le=1440)
    period_from: Optional[str] = None
    period_to: Optional[str] = None
    query: Optional[str] = Field(default=None, max_length=500)


class Embedding(BaseModel):
    model: str
    dim: int = Field(ge=1, le=8192)
    values: list[float] = Field(min_length=1, max_length=8192)

    @field_validator("values")
    @classmethod
    def check_dim(cls, v, info):
        return v


class InterpretResponse(BaseModel):
    turn_id: str
    action: Action
    confidence: float = Field(ge=0, le=1)
    entities: Optional[Entities] = None
    missing: Optional[list[Literal["description", "datetime", "time", "query"]]] = None
    question: Optional[str] = Field(default=None, max_length=300)
    embedding: Optional[Embedding] = None
    provider: Optional[str] = None
    model: Optional[str] = None

    @model_validator(mode="after")
    def check_invariants(self):
        a = self.action

        # search требует entities.query и embedding
        if a == Action.search:
            if not self.entities or not self.entities.query:
                raise ValueError("search требует entities.query")
            if not self.embedding:
                raise ValueError("search требует embedding")

        # clarify требует missing и question
        if a == Action.clarify:
            if not self.missing or not self.question:
                raise ValueError("clarify требует missing и question")

        # unknown без entities
        if a == Action.unknown and self.entities is not None:
            raise ValueError("unknown без entities")

        # create_meeting требует starts_on и starts_at — иначе clarify
        if a == Action.create_meeting:
            if not self.entities or not self.entities.starts_on or not self.entities.starts_at:
                raise ValueError("create_meeting требует starts_on и starts_at")

        # starts_at без starts_on запрещён
        if self.entities and self.entities.starts_at and not self.entities.starts_on:
            raise ValueError("starts_at без starts_on запрещён")

        # description и embedding — пара
        if self.entities and self.entities.description and not self.embedding:
            raise ValueError("описание без вектора")

        return self
