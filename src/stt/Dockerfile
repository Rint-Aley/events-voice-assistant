FROM python:3.12-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg libsndfile1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

RUN python -c "from faster_whisper import WhisperModel; WhisperModel('base', device='cpu', compute_type='int8')"

COPY app.py .

ENV STT_MODEL=base \
    STT_DEADLINE_SECONDS=25 \
    PYTHONUNBUFFERED=1

EXPOSE 8010

HEALTHCHECK --interval=10s --timeout=3s --start-period=90s \
  CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8010/health')"

CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8010"]