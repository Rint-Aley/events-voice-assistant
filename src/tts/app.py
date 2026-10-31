import asyncio
import hashlib
import logging
import os
import subprocess
import tempfile
import time
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Literal
from uuid import UUID

import boto3
import edge_tts
from botocore.exceptions import ClientError
from botocore.config import Config
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("tts_service")

TTS_DEFAULT_VOICE = os.getenv("TTS_DEFAULT_VOICE", "ru-RU-SvetlanaNeural")
TTS_DEFAULT_FORMAT = os.getenv("TTS_DEFAULT_FORMAT", "mp3")
TTS_TIMEOUT_SECONDS = int(os.getenv("TTS_TIMEOUT_SECONDS", "20"))

S3_ENDPOINT = os.getenv("S3_ENDPOINT", "http://s3:9000")
S3_ACCESS_KEY = os.getenv("S3_ACCESS_KEY", "todo-s3")
S3_SECRET_KEY = os.getenv("S3_SECRET_KEY", "todo-s3-secret")
S3_BUCKET_TTS = os.getenv("S3_BUCKET_TTS", "tts")
S3_REGION = os.getenv("S3_REGION", "us-east-1")

ALLOWED_FORMATS = ("mp3", "ogg_opus", "wav")
if TTS_DEFAULT_FORMAT not in ALLOWED_FORMATS: raise ValueError(f"Invalid TTS_DEFAULT_FORMAT")

s3_config = Config(signature_version="s3v4", connect_timeout=2, read_timeout=5, retries={'max_attempts': 1})
s3_client = boto3.client("s3", endpoint_url=S3_ENDPOINT, aws_access_key_id=S3_ACCESS_KEY,
                         aws_secret_access_key=S3_SECRET_KEY, region_name=S3_REGION, config=s3_config)

_locks: dict[str, tuple[asyncio.Lock, float]] = {}
_tts_sem = asyncio.Semaphore(3)


async def _clean_locks_loop():
    while True:
        await asyncio.sleep(3600)
        current_time = time.time()
        keys_to_remove = [k for k, (lock, ts) in _locks.items() if not lock.locked() and (current_time - ts > 3600)]
        for k in keys_to_remove: del _locks[k]
        if keys_to_remove: logger.info(f"Очищено {len(keys_to_remove)} локов TTS")


@asynccontextmanager
async def lifespan(app: FastAPI):
    cleanup_task = asyncio.create_task(_clean_locks_loop())
    yield
    cleanup_task.cancel()


app = FastAPI(title="TTS Service", lifespan=lifespan)


class TTSRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    answer_id: UUID
    text: str = Field(min_length=1, max_length=1000)
    voice: str = Field(default=TTS_DEFAULT_VOICE, min_length=1, max_length=64)
    format: Literal["mp3", "ogg_opus", "wav"] = TTS_DEFAULT_FORMAT
    speed: float = Field(default=1.0, ge=0.5, le=2.0)


@app.exception_handler(RequestValidationError)
async def validation_handler(request: Request, exc: RequestValidationError):
    message = "; ".join(f"{'.'.join(str(p) for p in e['loc'])}: {e['msg']}" for e in exc.errors())
    return JSONResponse(status_code=400,
                        content={"error": {"code": "BAD_REQUEST", "message": message[:500], "retryable": False}})


@app.get("/health")
async def health_check():
    try:
        await asyncio.to_thread(s3_client.head_bucket, Bucket=S3_BUCKET_TTS)
        return {"status": "ok"}
    except Exception as e:
        logger.error(f"Healthcheck S3 failed: {e}")
        return JSONResponse(status_code=503, content={"status": "error", "message": "S3 unavailable"})


async def _synthesize_to_file(text: str, voice: str, speed: float, dest: Path) -> None:
    if abs(speed - 1.0) < 0.01:
        communicate = edge_tts.Communicate(text, voice)
    else:
        communicate = edge_tts.Communicate(text, voice, rate=f"{int(round((speed - 1) * 100)):+d}%")
    await communicate.save(str(dest))


def _convert_format_sync(src: Path, dst: Path, fmt: str) -> None:
    if fmt == "ogg_opus":
        cmd = ["ffmpeg", "-y", "-i", str(src), "-c:a", "libopus", "-f", "ogg", str(dst)]
    else:
        cmd = ["ffmpeg", "-y", "-i", str(src), "-c:a", "pcm_s16le", str(dst)]
    subprocess.run(cmd, check=True, capture_output=True)


def _probe_duration_ms_sync(path: Path) -> int:
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1",
         str(path)], capture_output=True, text=True, check=True)
    return int(float(result.stdout.strip() or "0") * 1000)


def _check_s3_object(key: str, expected_content_type: str) -> int | None:
    try:
        response = s3_client.head_object(Bucket=S3_BUCKET_TTS, Key=key)
        if response.get('ContentType') != expected_content_type:
            return None

        duration = int(response.get('Metadata', {}).get('duration_ms', 0))
        if duration > 0:
            return duration

        logger.info(f"Файл {key} есть в S3, но без метаданных. Извлекаю длительность...")
        with tempfile.NamedTemporaryFile(delete=False) as tmp:
            s3_client.download_fileobj(S3_BUCKET_TTS, key, tmp)
            tmp_path = tmp.name
        try:
            return _probe_duration_ms_sync(Path(tmp_path))
        finally:
            os.unlink(tmp_path)

    except ClientError as e:
        if e.response['Error']['Code'] == '404': return None
        logger.error(f"S3 head_object error for {key}: {e.response}")
        raise


def _upload_to_s3(local_path: Path, key: str, content_type: str, duration_ms: int) -> None:
    try:
        s3_client.upload_file(Filename=str(local_path), Bucket=S3_BUCKET_TTS, Key=key,
                              ExtraArgs={'ContentType': content_type, 'Metadata': {'duration_ms': str(duration_ms)}})
    except ClientError as e:
        logger.error(f"S3 upload error for {key}: {e.response}")
        raise


async def _synthesize_impl(req: TTSRequest):
    if not req.text.strip():
        return JSONResponse(status_code=422,
                            content={"error": {"code": "EMPTY_TEXT", "message": "Пустой текст", "retryable": False}})

    key_hash = hashlib.sha256(f"{req.text}\x1f{req.voice}\x1f{req.format}\x1f{req.speed}".encode("utf-8")).hexdigest()
    ext = {"mp3": "mp3", "ogg_opus": "opus", "wav": "wav"}[req.format]
    object_key = f"tts/{key_hash}.{ext}"
    content_type = {"mp3": "audio/mpeg", "ogg_opus": "audio/ogg", "wav": "audio/wav"}[req.format]

    if key_hash in _locks:
        lock, _ = _locks[key_hash]
    else:
        lock = asyncio.Lock()
        _locks[key_hash] = (lock, time.time())

    async with lock:
        try:
            cached_duration = await asyncio.to_thread(_check_s3_object, object_key, content_type)
        except Exception as e:
            return JSONResponse(status_code=503, content={
                "error": {"code": "PROVIDER_UNAVAILABLE", "message": f"S3 недоступен: {e}"[:500], "retryable": True,
                          "details": {"provider": "s3"}}})

        if cached_duration is not None:
            return {"answer_id": str(req.answer_id), "object_key": object_key, "content_type": content_type,
                    "format": req.format, "duration_ms": cached_duration, "cached": True}

        fd_src, tmp_src_name = tempfile.mkstemp(suffix=".src.mp3");
        os.close(fd_src);
        tmp_src = Path(tmp_src_name)
        fd_dst, tmp_dst_name = tempfile.mkstemp(suffix=f".{ext}");
        os.close(fd_dst);
        tmp_dst = Path(tmp_dst_name)

        try:
            async with _tts_sem:
                try:
                    await asyncio.wait_for(_synthesize_to_file(req.text, req.voice, req.speed, tmp_src), timeout=15)
                except asyncio.TimeoutError:
                    return JSONResponse(status_code=503, content={
                        "error": {"code": "PROVIDER_UNAVAILABLE", "message": "Синтез превысил 15 с", "retryable": True,
                                  "details": {"provider": "edge-tts"}}})
                except Exception as e:
                    return JSONResponse(status_code=503, content={
                        "error": {"code": "PROVIDER_UNAVAILABLE", "message": f"Синтез не удался: {e}"[:500],
                                  "retryable": True, "details": {"provider": "edge-tts"}}})

                try:
                    if req.format == "mp3":
                        os.replace(tmp_src, tmp_dst)
                    else:
                        await asyncio.wait_for(asyncio.to_thread(_convert_format_sync, tmp_src, tmp_dst, req.format),
                                               timeout=10)
                        tmp_src.unlink(missing_ok=True)
                except asyncio.TimeoutError:
                    return JSONResponse(status_code=503, content={
                        "error": {"code": "PROVIDER_UNAVAILABLE", "message": "Конвертация превысила 10 с",
                                  "retryable": True, "details": {"provider": "ffmpeg"}}})
                except subprocess.CalledProcessError as e:
                    return JSONResponse(status_code=422, content={
                        "error": {"code": "UNSUPPORTED_FORMAT", "message": f"Ошибка ffmpeg: {e}"[:500],
                                  "retryable": False}})
                except Exception as e:
                    return JSONResponse(status_code=500, content={
                        "error": {"code": "INTERNAL", "message": f"Конвертация не удалась: {e}"[:500],
                                  "retryable": True}})

            try:
                duration_ms = await asyncio.to_thread(_probe_duration_ms_sync, tmp_dst)
                await asyncio.to_thread(_upload_to_s3, tmp_dst, object_key, content_type, duration_ms)
            except Exception as e:
                return JSONResponse(status_code=503, content={
                    "error": {"code": "PROVIDER_UNAVAILABLE", "message": f"Загрузка в S3 не удалась: {e}"[:500],
                              "retryable": True, "details": {"provider": "s3"}}})

            return {"answer_id": str(req.answer_id), "object_key": object_key, "content_type": content_type,
                    "format": req.format, "duration_ms": duration_ms, "cached": False}
        finally:
            tmp_src.unlink(missing_ok=True)
            tmp_dst.unlink(missing_ok=True)
            if key_hash in _locks and not _locks[key_hash][0].locked(): _locks[key_hash] = (_locks[key_hash][0],
                                                                                            time.time())


@app.post("/v1/synthesize")
async def synthesize(req: TTSRequest):
    try:
        return await asyncio.wait_for(_synthesize_impl(req), timeout=TTS_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        logger.error(f"TTS общий таймаут {TTS_TIMEOUT_SECONDS} сек")
        return JSONResponse(status_code=504, content={
            "error": {"code": "DEADLINE_EXCEEDED", "message": f"Общий таймаут {TTS_TIMEOUT_SECONDS}с",
                      "retryable": True}})