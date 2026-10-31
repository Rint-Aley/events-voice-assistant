from __future__ import annotations

from fastapi import FastAPI, HTTPException

from .interpret import interpret
from .schemas import InterpretRequest, InterpretResponse

app = FastAPI(title="NLU service", version="1.0.0")


@app.post("/v1/interpret", response_model=InterpretResponse)
def interpret_endpoint(req: InterpretRequest) -> InterpretResponse:
    try:
        return interpret(req)
    except ValueError as e:
        raise HTTPException(status_code=422, detail=str(e))


@app.get("/health")
def health():
    return {"status": "ok"}
