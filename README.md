# Smart-Glasses-Middleware

This repository contains the backend middleware for a smart glasses application. It provides a powerful, multi-modal API for interacting with AI models, processing documents, and managing tasks. The system is designed to be extensible, allowing developers to add new capabilities with ease.

## Features

- **Multi-modal API**: Accepts requests via text, audio, images, and video.
- **AI Task Detection & Routing**: Uses a local AI model (Ollama) with Microsoft Semantic Kernel to classify incoming tasks. Simple tasks are handled locally, while complex queries are intelligently routed to a more advanced AI model.
- **Extensible Task System**: Dynamically load custom task handlers by dropping C# source files or compiled DLLs into the `tasks` directory. This allows for easy extension of the middleware's capabilities without needing to recompile the main application. See the [Task Handlers](#task-handlers) section for more details.
- **Advanced RAG Pipeline**: A sophisticated Retrieval-Augmented Generation pipeline for document interaction.
  - **Multi-format Document Processing**: Extracts text and layout information from PDFs (digital and scanned), DOCX, TXT, and various image formats.
  - **Vector Embeddings**: Generates embeddings for document chunks using configurable models and stores them in PostgreSQL with `pgvector` for efficient similarity searches.
  - **Semantic Querying**: Understands the meaning behind your questions to find the most relevant information.
  - **Document Highlighting**: Pinpoints and highlights the exact location of information within your documents.
  - **Resource Groups**: Organize documents and prompts into groups for targeted querying.
  - For more details, see the [RAG Process Documentation](docs/RAG_PROCESS.md).
- **Comprehensive Admin API**: A rich set of endpoints for managing the system's resources, including documents, prompts, and resource groups.
- **Real-time Notifications**: Uses SignalR and Redis Pub/Sub to provide real-time notifications about background task completion.
- **Robust Storage**:
  - **PostgreSQL**: For storing request history, document metadata, and vector embeddings.
  - **MinIO**: For scalable storage of media files (images, videos, audio) and documents.
  - **Redis**: For response caching and pub/sub messaging.
- **Monitoring & Health Checks**: Includes `/health` and `/metrics` endpoints for monitoring the application's status and performance.

## Architecture

The application is built on a modern .NET 8 stack, with a focus on modularity and scalability.

- **ASP.NET Core Web API**: Provides the core framework for the RESTful API.
- **Microsoft Semantic Kernel**: For AI orchestration, prompt engineering, and service connections.
- **Ollama**: Serves as the local, lightweight AI model for initial task processing and simple queries.
- **PostgreSQL + pgvector**: For relational data storage and efficient vector similarity search.
- **MinIO**: S3-compatible object storage for all media and document files.
- **Redis**: For caching, background task management, and real-time messaging.
- **SignalR**: For pushing real-time notifications to connected clients.

## Quick Start with Docker

The easiest way to get started is by using the provided Docker Compose file, which sets up the entire stack.

```bash
# From the repository root
docker-compose pull
docker-compose up --build
```

The API will then be available at `http://localhost:5000`.

## Configuration

The application is configured via `src/SmartGlassesMiddleware/appsettings.json` and environment variables. Key settings include:

- `ConnectionStrings`: For PostgreSQL and Redis.
- `MinIO`: For MinIO server connection details.
- `AI`: For configuring Ollama and the advanced AI model endpoints.
- `RAG`: For tuning the RAG pipeline's behavior (e.g., token limits, embedding settings).
- `Transcription`: For the server-side transcription service endpoint.

Secrets should be managed using Docker secrets or another secure method. The application can read secrets from files in `/run/secrets` and map them to configuration keys. See `Program.cs` for details on the secret mapping logic.

## API Endpoints

The application exposes a comprehensive set of RESTful API endpoints. For a complete and interactive API specification, run the application and navigate to `/swagger`. The OpenAPI specification can be found at `openapi.json`.

### Main Endpoints
- `POST /api/text`: Submit a text-based query.
- `POST /api/transcribe`: Submit an audio file for transcription and processing.
- `POST /api/ask`: Submit a media file (image/video) with a prompt.
- `GET /api/history`: Retrieve request history.

### Document (RAG) Endpoints
- `POST /api/documents/upload`: Upload a document for processing.
- `POST /api/documents/query`: Query your documents.
- `GET /api/documents`: List uploaded documents.
- `GET /api/documents/{id}`: Get details for a specific document.
- See the [RAG Process Documentation](docs/RAG_PROCESS.md) for more.

### Admin Endpoints
- `GET, POST, PUT, DELETE /api/admin/files`: Manage uploaded files.
- `GET, POST, PUT, DELETE /api/admin/prompts`: Manage saved prompts.
- `GET, POST, PUT, DELETE /api/admin/groups`: Manage resource groups.
- See the `AdminController.cs` for a full list of admin endpoints.

### Task Management
- `GET /api/tasks/{id}`: Get the status of a background task.

## Task Handlers

The middleware can be extended with custom task handlers. These are C# classes that implement the `ITaskHandler` interface. The system automatically discovers and registers any `ITaskHandler` implementations found in `.cs` or `.dll` files in the `tasks` directory at startup.

### Creating a Task Handler

1.  Create a new C# class that implements `SmartGlassesMiddleware.Services.ITaskHandler`.
2.  Implement the `CanHandle` method to specify which tasks your handler can process (based on a task name string).
3.  Implement the `HandleAsync` method to perform the task's logic.
4.  Place the `.cs` file (or the compiled `.dll`) in the `tasks` directory.

The application will automatically pick up your new handler on the next restart. See the existing handlers in the `tasks` directory for examples.

## Development

### Prerequisites
- .NET 8.0 SDK
- Docker (for dependencies)

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