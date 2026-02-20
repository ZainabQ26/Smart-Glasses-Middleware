FROM python:3.11-slim-bookworm

ENV PYTHONUNBUFFERED=1

# Update system packages and install needed runtime build deps. Upgrading packages
# at build time reduces known vulnerabilities in base image packages.
RUN apt-get update && apt-get upgrade -y && apt-get install -y --no-install-recommends \
    ffmpeg \
    build-essential \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
# Ensure pip is recent, then install torch and other Python deps.
RUN python -m pip install --upgrade pip && \
    pip install --no-cache-dir torch==2.0.0 --index-url https://download.pytorch.org/whl/cpu && \
    pip install --no-cache-dir openai-whisper ffmpeg-python boto3

COPY handler.py .

CMD ["/usr/local/bin/python", "handler.py"]
