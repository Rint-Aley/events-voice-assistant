import asyncio
import logging
import math
import os
import subprocess
import tempfile
import time
from collections import OrderedDict
from contextlib import asynccontextmanager
from typing import Literal
from uuid import UUID

import httpx
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field, HttpUrl
from faster_whisper import WhisperModel

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("stt_service")

STT_MODEL = os.getenv("STT_MODEL", "base")
STT_DEADLINE_SECONDS = int(os.getenv("STT_DEADLINE_SECONDS", "25"))
MAX_AUDIO_SIZE_BYTES = 10 * 1024 * 1024

model = None
_stt_sem = asyncio.Semaphore(2)
_stt_cache: OrderedDict = OrderedDict()
_in_progress: dict[str, asyncio.Future] = {}


async def _clean_cache_loop():
    while True:
        await asyncio.sleep(60)
        current_time = time.time()
        keys_to_remove = [k for k, v in _stt_cache.items() if current_time - v["timestamp"] > 600]
        for k in keys_to_remove:
            del _stt_cache[k]
        if keys_to_remove:
            logger.info(f"Очищено {len(keys_to_remove)} записей из кэша STT")


@asynccontextmanager
async def lifespan(app: FastAPI):
    global model
    logger.info(f"Загружаю модель Whisper ({STT_MODEL})...")
    model = WhisperModel(STT_MODEL, device="cpu", compute_type="int8")
    logger.info("Модель загружена. Сервис готов к работе.")
    cleanup_task = asyncio.create_task(_clean_cache_loop())
    yield
    cleanup_task.cancel()
    logger.info("Останавливаю STT сервис...")


app = FastAPI(title="STT Service", lifespan=lifespan)


class STTRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    turn_id: UUID
    audio_url: HttpUrl
    content_type: Literal["audio/webm", "audio/ogg", "audio/wav", "audio/mpeg"]
    language: Literal["ru"] = "ru"
    max_duration_ms: int = Field(default=60000, ge=1000, le=120000)


@app.exception_handler(RequestValidationError)
async def validation_handler(request: Request, exc: RequestValidationError):
    errs = [{"type": e["type"], "loc": e["loc"], "msg": e["msg"]} for e in exc.errors()]
    return JSONResponse(status_code=400, content={
        "error": {"code": "BAD_REQUEST", "message": str(errs)[:500], "retryable": False}
    })


@app.get("/health")
def health_check():
    if model is None:
        return JSONResponse(status_code=503, content={"status": "error", "message": "Model not loaded"})
    return {"status": "ok"}


def _parse_seconds(raw: str) -> float | None:
    """Разбирает значение секунд из ffprobe. Пустое, N/A и NaN дают None."""
    text = (raw or "").strip()
    if not text:
        return None
    try:
        seconds = float(text)
    except ValueError:
        return None
    if not math.isfinite(seconds):
        return None
    return seconds


def _parse_duration_ms(raw: str) -> int | None:
    """Длительность из format=duration. Ноль и отрицательные значения невалидны."""
    seconds = _parse_seconds(raw)
    if seconds is None or seconds <= 0:
        return None
    return int(round(seconds * 1000))


def _parse_packet_duration_ms(output: str) -> int | None:
    """
    Длительность по аудиопакетам: максимум (pts_time + duration_time).

    Нужна для WebM от MediaRecorder, где format=duration часто отсутствует.
    Строки с невалидным pts пропускаются; отсутствующая duration_time считается нулём.
    """
    best_ms = None
    for line in output.splitlines():
        fields = line.strip().split(",")
        if len(fields) < 2:
            continue

        pts = _parse_seconds(fields[0])
        duration = _parse_seconds(fields[1])
        if pts is None or pts < 0:
            continue

        end_ms = int(round((pts + max(duration or 0.0, 0.0)) * 1000))
        if end_ms > 0 and (best_ms is None or end_ms > best_ms):
            best_ms = end_ms
    return best_ms


def _run_ffprobe(args: list[str]) -> str:
    result = subprocess.run(
        ["ffprobe", "-v", "error", *args],
        capture_output=True, text=True, check=False,
    )
    return result.stdout if result.returncode == 0 else ""


def _probe_duration_ms_sync(path: str) -> int:
    format_output = _run_ffprobe([
        "-show_entries", "format=duration",
        "-of", "default=noprint_wrappers=1:nokey=1",
        path,
    ])
    duration_ms = _parse_duration_ms(format_output)
    if duration_ms is not None:
        return duration_ms

    # WebM без duration metadata: берём конец последнего аудиопакета.
    packet_output = _run_ffprobe([
        "-select_streams", "a:0",
        "-show_entries", "packet=pts_time,duration_time",
        "-of", "csv=p=0",
        path,
    ])
    duration_ms = _parse_packet_duration_ms(packet_output)
    if duration_ms is not None:
        return duration_ms

    raise ValueError("Не удалось определить длительность (битый файл)")


async def _probe_duration_ms(path: str) -> int | None:
    try:
        return await asyncio.to_thread(_probe_duration_ms_sync, path)
    except Exception:
        return None


def _transcribe_sync(path: str):
    try:
        segments, info = model.transcribe(
            path, language="ru", vad_filter=True, vad_parameters=dict(min_silence_duration_ms=500)
        )
    except MemoryError as e:
        raise RuntimeError(f"Нехватка памяти при декодировании: {e}")
    except Exception as e:
        raise ValueError(f"Файл не является аудио: {e}")

    text_parts, total_logprob, segment_count = [], 0.0, 0
    for seg in segments:
        text_parts.append(seg.text.strip())
        total_logprob += seg.avg_logprob
        segment_count += 1

    text = " ".join(text_parts).strip()
    confidence = min(1.0, max(0.0, math.exp(total_logprob / segment_count))) if segment_count > 0 else 0.0
    if len(text) > 1000:
        cut = text[:1000]
        text = cut[:cut.rfind(" ")] if " " in cut else cut
    return text, info, confidence


async def _download_audio(url: str) -> bytes:
    timeout = httpx.Timeout(20.0, connect=5.0)
    async with httpx.AsyncClient(timeout=timeout, follow_redirects=True) as client:
        async with client.stream("GET", url) as response:
            if response.status_code >= 400:
                raise httpx.HTTPStatusError("HTTP Error", request=response.request, response=response)

            content_length = int(response.headers.get("Content-Length", 0))
            if content_length > MAX_AUDIO_SIZE_BYTES:
                raise ValueError("PAYLOAD_TOO_LARGE")

            audio_bytes = b""
            async for chunk in response.aiter_bytes():
                audio_bytes += chunk
                if len(audio_bytes) > MAX_AUDIO_SIZE_BYTES:
                    raise ValueError("PAYLOAD_TOO_LARGE")
            if len(audio_bytes) == 0:
                raise ValueError("EMPTY_AUDIO")
            return audio_bytes


async def _transcribe_impl(req: STTRequest):
    logger.info(f"[{req.turn_id}] Начинаю обработку")
    try:
        audio_bytes = await _download_audio(str(req.audio_url))
    except httpx.HTTPStatusError as e:
        if 400 <= e.response.status_code < 500:
            return JSONResponse(status_code=422, content={
                "error": {"code": "AUDIO_UNREADABLE", "message": f"HTTP {e.response.status_code}", "retryable": False}})
        return JSONResponse(status_code=503, content={
            "error": {"code": "PROVIDER_UNAVAILABLE", "message": str(e)[:500], "retryable": True,
                      "details": {"provider": "http"}}})
    except ValueError as e:
        if str(e) == "PAYLOAD_TOO_LARGE":
            return JSONResponse(status_code=413, content={
                "error": {"code": "PAYLOAD_TOO_LARGE", "message": "Файл больше 10 МБ", "retryable": False}})
        return JSONResponse(status_code=422, content={
            "error": {"code": "AUDIO_UNREADABLE", "message": str(e)[:500], "retryable": False}})
    except Exception as e:
        return JSONResponse(status_code=503, content={
            "error": {"code": "PROVIDER_UNAVAILABLE", "message": str(e)[:500], "retryable": True,
                      "details": {"provider": "http"}}})

    suffix_map = {"audio/webm": ".webm", "audio/ogg": ".ogg", "audio/wav": ".wav", "audio/mpeg": ".mp3"}
    with tempfile.NamedTemporaryFile(delete=False, suffix=suffix_map[req.content_type]) as tmp:
        tmp.write(audio_bytes)
        tmp_path = tmp.name

    task_done = False
    try:
        probed_ms = await _probe_duration_ms(tmp_path)

        if probed_ms is None:
            return JSONResponse(status_code=422, content={
                "error": {"code": "AUDIO_UNREADABLE", "message": "Не удалось определить длительность (битый файл)",
                          "retryable": False}})

        if probed_ms > req.max_duration_ms:
            return JSONResponse(status_code=422, content={
                "error": {"code": "AUDIO_TOO_LONG", "message": f"Аудио {probed_ms} мс > {req.max_duration_ms} мс",
                          "retryable": False}})

        await _stt_sem.acquire()
        try:
            text, info, confidence = await asyncio.to_thread(_transcribe_sync, tmp_path)
            task_done = True
        finally:
            _stt_sem.release()

    except ValueError as e:
        return JSONResponse(status_code=422, content={
            "error": {"code": "AUDIO_UNREADABLE", "message": str(e)[:500], "retryable": False}})
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={
            "error": {"code": "PROVIDER_UNAVAILABLE", "message": str(e)[:500], "retryable": True,
                      "details": {"provider": "whisper"}}})
    except Exception as e:
        logger.error(f"[{req.turn_id}] Ошибка: {e}", exc_info=True)
        return JSONResponse(status_code=500,
                            content={"error": {"code": "INTERNAL", "message": str(e)[:500], "retryable": True}})
    finally:
        if task_done:
            try:
                os.unlink(tmp_path)
            except OSError:
                pass

    duration_ms = min(probed_ms, 120000)
    logger.info(f"[{req.turn_id}] Успех. Длительность: {duration_ms} мс, confidence: {confidence:.2f}")

    response_data = {
        "turn_id": str(req.turn_id), "text": text, "language": "ru", "confidence": round(confidence, 3),
        "empty": len(text) == 0, "duration_ms": duration_ms, "provider": "whisper", "model": STT_MODEL,
    }

    _stt_cache[str(req.turn_id)] = {"data": response_data, "timestamp": time.time()}
    _stt_cache.move_to_end(str(req.turn_id))
    if len(_stt_cache) > 1000: _stt_cache.popitem(last=False)
    return response_data


@app.post("/v1/transcribe")
async def transcribe(req: STTRequest):
    turn_id_str = str(req.turn_id)
    cached = _stt_cache.get(turn_id_str)
    if cached and (time.time() - cached["timestamp"] < 600):
        _stt_cache.move_to_end(turn_id_str)
        return cached["data"]

    if turn_id_str in _in_progress:
        return await _in_progress[turn_id_str]

    fut = asyncio.get_running_loop().create_future()
    _in_progress[turn_id_str] = fut

    try:
        result = await asyncio.wait_for(_transcribe_impl(req), timeout=STT_DEADLINE_SECONDS)
        fut.set_result(result)
        return result
    except asyncio.TimeoutError:
        err_resp = JSONResponse(status_code=504, content={
            "error": {"code": "DEADLINE_EXCEEDED", "message": f"Таймаут {STT_DEADLINE_SECONDS}с", "retryable": True}})
        fut.set_result(err_resp)
        return err_resp
    except Exception as e:
        fut.set_exception(e)
        raise
    finally:
        del _in_progress[turn_id_str]