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
- Ollama server (local model runtime)

Note: a lightweight server-side transcription service (`vosk-transcriber`) is included in the provided `docker-compose.yml` and will auto-download a small Vosk model at startup. No external transcription keys are required for that service.

## Quick Start with Docker

The repository includes a `docker-compose.yml` that starts the full stack (Postgres, Redis, MinIO, Ollama, Vosk transcriber, and the app). To start everything locally run:

```bash
# from the repository root
docker compose pull
docker compose up --build
```

The `vosk-transcriber` service auto-downloads a small Vosk model into the shared `models` Docker volume on first start; no extra steps or API keys are required for basic transcription.

Build/run the application locally (dev):

```bash
cd src/SmartGlassesMiddleware
dotnet restore
dotnet build
dotnet run
```

The API will be available at `https://localhost:5001` or `http://localhost:5000` (or `http://localhost:5000` when run via Docker compose).

## Configuration

Edit `appsettings.json` to configure the services. In addition to the existing settings, the app reads a `Transcription:Endpoint` configuration key (or the environment variable `Transcription__Endpoint`) to know where to send audio for server-side transcription. By default the docker-compose sets this to the Vosk transcriber:

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
  },
  "Transcription": {
    "Endpoint": "http://vosk-transcriber:8080/transcribe"
  }
}
```

If you prefer to point to another transcription service, override `Transcription__Endpoint` or set the `Transcription:Endpoint` key.

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
prompt: "What's in this image?"
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
