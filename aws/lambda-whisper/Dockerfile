FROM python:3.11-slim

ENV PYTHONUNBUFFERED=1

RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    build-essential \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir torch==2.0.0 --index-url https://download.pytorch.org/whl/cpu && \
    pip install --no-cache-dir openai-whisper ffmpeg-python boto3

COPY handler.py .

CMD ["/usr/local/bin/python", "handler.py"]
