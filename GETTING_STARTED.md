# Getting Started Guide

This guide will help you set up and run the Smart Glasses Middleware application.

## Prerequisites

Before you begin, ensure you have the following installed:
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/get-started) and Docker Compose
- Git

## Step 1: Clone the Repository

```bash
git clone https://github.com/robertbiv/Smart-Glasses-Middleware.git
cd Smart-Glasses-Middleware
```

## Step 2: Start the Infrastructure Services

The application requires PostgreSQL, Redis, MinIO, and Ollama. These are all configured in the `docker-compose.yml` file.

```bash
docker-compose up -d
```

This will start:
- PostgreSQL on port 5432
- Redis on port 6379
- MinIO on ports 9000 (API) and 9001 (Console)
- Ollama on port 11434

## Step 3: Setup Ollama Model

After the containers are running, you need to pull the Ollama model:

```bash
./setup-ollama.sh
```

Or manually:
```bash
docker exec smartglasses-ollama ollama pull llama2
```

## Step 4: Build and Run the Application

```bash
cd src/SmartGlassesMiddleware
dotnet restore
dotnet build
dotnet run
```

The API will be available at:
- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`

## Step 5: Test the API

You can test the API using curl, Postman, or the included HTTP file.

### Using curl

**Text Request:**
```bash
curl -X POST http://localhost:5000/api/text \
  -H "Content-Type: application/json" \
  -d '{"text": "What time is it?"}'
```

**Audio Request:**
```bash
curl -X POST http://localhost:5000/api/audio \
  -H "Content-Type: application/json" \
  -d '{
    "audioBase64": "UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=",
    "transcription": "What is the weather?"
  }'
```

**Get History:**
```bash
curl http://localhost:5000/api/history?count=10
```

### Using the HTTP file

If you're using Visual Studio Code with the REST Client extension or Visual Studio:
1. Open `src/SmartGlassesMiddleware/SmartGlassesMiddleware.http`
2. Click "Send Request" above each endpoint

## Accessing the Services

### MinIO Console
- URL: http://localhost:9001
- Username: minioadmin
- Password: minioadmin

### PostgreSQL
- Host: localhost
- Port: 5432
- Database: smartglasses
- Username: postgres
- Password: postgres

### Redis
- Host: localhost
- Port: 6379

## Configuration

You can customize the configuration in `appsettings.json`:

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

## Troubleshooting

### Ollama Connection Issues
If you get connection errors to Ollama:
1. Check if the container is running: `docker ps | grep ollama`
2. Check the logs: `docker logs smartglasses-ollama`
3. Verify the model is pulled: `docker exec smartglasses-ollama ollama list`

### Database Issues
If you get database connection errors:
1. Check if PostgreSQL is running: `docker ps | grep postgres`
2. Try connecting manually: `docker exec -it smartglasses-postgres psql -U postgres`
3. Check the logs: `docker logs smartglasses-postgres`

### MinIO Issues
If file uploads fail:
1. Check if MinIO is running: `docker ps | grep minio`
2. Access the console at http://localhost:9001
3. Check the logs: `docker logs smartglasses-minio`

## Stopping the Services

To stop all services:
```bash
docker-compose down
```

To remove all data:
```bash
docker-compose down -v
```

## Next Steps

- Check the [README.md](README.md) for more detailed information
- Explore the API endpoints in the controllers
- Customize the task detection logic in `Services/AIService.cs`
- Add more task types to the known tasks list
