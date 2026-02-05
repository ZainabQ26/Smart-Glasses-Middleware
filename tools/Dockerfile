# Dockerfile for Vosk Speech Recognition Server
FROM python:3.11-slim

# Install system dependencies including curl for healthcheck
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    libsndfile1 \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Install Python dependencies
RUN pip install --no-cache-dir \
    flask \
    requests \
    vosk \
    soundfile

# Create directory for models
RUN mkdir -p /models

# Copy the server script
COPY vosk_server.py /app/vosk_server.py

# Set working directory
WORKDIR /app

# Set default environment variables
ENV VOSK_MODEL_NAME=vosk-model-small-en-us-0.15
ENV MODEL_DIR=/models/vosk-model-small-en-us-0.15
ENV VOSK_MODEL_URL=https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip
ENV PORT=8080

# Expose the service port
EXPOSE 8080

# Run the server
CMD ["python", "/app/vosk_server.py"]
