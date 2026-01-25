# Smart-Glasses-Middleware

The middleware for the smart glasses that will run the small AI to complete simple tasks, and for other tasks it will send it to the stronger AI model.

## Features

- **Multi-format API**: Accepts text, audio, images, and video requests
- **AI Task Detection**: Uses Ollama with Microsoft Semantic Kernel to classify and process tasks
- **Intelligent Routing**: Automatically detects if a task requires a stronger model and routes accordingly
- **Storage**: 
  - PostgreSQL for request history
  - MinIO for media storage (images, videos, audio)
  - Redis for caching responses
- **RESTful API**: Clean, well-documented endpoints

## Architecture

The application is built with:
- **ASP.NET Core Web API** (.NET 10.0)
- **Microsoft Semantic Kernel** for AI orchestration
- **Ollama** as the local lightweight AI model
- **PostgreSQL** for relational data storage
- **MinIO** for object storage
- **Redis** for caching

## Prerequisites

- .NET 10.0 SDK
- Docker (recommended for running dependencies)
- PostgreSQL database
- Redis server
- MinIO server
- Ollama server

## Quick Start with Docker

1. Start the required services using Docker:

```bash
# PostgreSQL
docker run -d --name postgres \
  -e POSTGRES_DB=smartglasses \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 \
  postgres:latest

# Redis
docker run -d --name redis \
  -p 6379:6379 \
  redis:latest

# MinIO
docker run -d --name minio \
  -p 9000:9000 -p 9001:9001 \
  -e MINIO_ROOT_USER=minioadmin \
  -e MINIO_ROOT_PASSWORD=minioadmin \
  quay.io/minio/minio server /data --console-address ":9001"

# Ollama
docker run -d --name ollama \
  -p 11434:11434 \
  -v ollama:/root/.ollama \
  ollama/ollama

# Pull the llama2 model
docker exec ollama ollama pull llama2
```

2. Build and run the application:

```bash
cd src/SmartGlassesMiddleware
dotnet restore
dotnet build
dotnet run
```

The API will be available at `https://localhost:5001` or `http://localhost:5000`.

## Configuration

Edit `appsettings.json` to configure the services:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=smartglasses;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "MinIO": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "BucketName": "smart-glasses-media"
  },
  "AI": {
    "OllamaEndpoint": "http://localhost:11434",
    "OllamaModel": "llama2",
    "StrongerModelEndpoint": "http://localhost:5000/api/chat"
  }
}
```

## API Endpoints

### Text Processing
```
POST /api/text
Content-Type: application/json

{
  "text": "What's the weather like today?"
}
```

### Audio Processing
```
POST /api/audio
Content-Type: application/json

{
  "audioBase64": "<base64-encoded-audio>",
  "transcription": "What time is it?"
}
```

### Media Processing (Images/Videos)
```
POST /api/media
Content-Type: multipart/form-data

file: <file-upload>
description: "What's in this image?"
```

### History
```
GET /api/history?count=10
GET /api/history/{id}
```

## How It Works

1. **Request Reception**: The API receives a request (text, audio, or media)
2. **Task Detection**: Ollama analyzes the request to determine the task type
3. **Task Classification**: 
   - Known tasks (weather, time, reminder, etc.) are processed by Ollama locally
   - Unknown or complex tasks are forwarded to a stronger model via REST API
4. **Response Generation**: The appropriate AI model generates a response
5. **Storage**: 
   - Request history is saved to PostgreSQL
   - Media files are stored in MinIO
   - Responses are cached in Redis for faster subsequent requests

## Supported Tasks

The local Ollama model can handle these tasks:
- Weather queries
- Time queries
- Reminders
- Alarms
- Timers
- Notes
- Translation
- Calculations

Other tasks are automatically routed to the stronger model.

## Development

### Build
```bash
dotnet build
```

### Run Tests
```bash
dotnet test
```

### Run Application
```bash
dotnet run --project src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj
```

## Project Structure

```
Smart-Glasses-Middleware/
├── src/
│   └── SmartGlassesMiddleware/
│       ├── Controllers/         # API endpoints
│       ├── Services/            # Business logic
│       ├── Models/              # Data models
│       ├── Data/                # Database context
│       └── Program.cs           # Application entry point
├── SmartGlassesMiddleware.sln   # Solution file
└── README.md
```

## License

MIT
