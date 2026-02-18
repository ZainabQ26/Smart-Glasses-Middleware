# Smart-Glasses-Middleware

This repository contains the backend middleware for a smart glasses application. It provides a powerful, multi-modal API for interacting with AI models, processing documents, and managing tasks. The system is designed to be extensible, allowing developers to add new capabilities with ease.

## Deployment Options

This application can be deployed in two modes:

### 🐳 Docker Compose (Development/Self-Hosted)
Quick setup for local development and self-hosted deployments using MinIO, Ollama, and containerized services.

**Quick Start:**
```bash
docker-compose up -d
```

### ☁️ AWS Cloud (Production)
Cloud-native deployment using AWS services (S3, Bedrock, RDS Aurora, ElastiCache, Lambda, ECS Fargate).

**Quick Start:**
```powershell
# 1. Configure AWS CLI
aws configure

# 2. Deploy infrastructure
cd aws/infrastructure
terraform init
terraform apply

# 3. Push Docker images
# (See full guide for details)

# 4. Deploy complete
```

📚 **For complete step-by-step AWS deployment, see:** [AWS_DEPLOYMENT_GUIDE.md](AWS_DEPLOYMENT_GUIDE.md)

📚 **For AWS quick reference, see:** [aws/AWS_QUICK_START.md](aws/AWS_QUICK_START.md)

## Frontend Options

The middleware provides a REST API and can be paired with any frontend. Two options are included:

### ⚡ Vite + React SPA (Recommended)
Lightweight (~80KB) client-side app optimized for voice commands and PDF viewing. Ideal for Android glasses with limited resources.

- **Location:** `client-app/`
- **Quick Start:** `.\start-client.ps1` or see [client-app/README.md](client-app/README.md)
- **Features:** Voice recording, PDF viewer, minimal bundle, fast load, TypeScript, Tailwind CSS
- **Best for:** Smart glasses, server-dependent apps, Android WebView, resource-constrained devices

### 📄 Static HTML Admin Pages
Simple HTML/JS admin and monitoring dashboards served from `wwwroot/`. Good for basic admin tasks without a build step.

- **Files:** `wwwroot/admin.html`, `wwwroot/monitoring.html`
- **Access:** Run the .NET app and visit `http://localhost:5000/admin.html`

## Features

  - **Multi-format Document Processing**: Extracts text and layout information from PDFs (digital and scanned), DOCX, TXT, and various image formats.
  - **Vector Embeddings**: Generates embeddings for document chunks using configurable models and stores them in PostgreSQL with `pgvector` for efficient similarity searches.
  - **Semantic Querying**: Understands the meaning behind your questions to find the most relevant information.
  - **Document Highlighting**: Pinpoints and highlights the exact location of information within your documents.
  - **Resource Groups**: Organize documents and prompts into groups for targeted querying.
  - For more details, see the [RAG Process Documentation](docs/RAG_PROCESS.md).
  - **PostgreSQL**: For storing request history, document metadata, and vector embeddings.
  - **MinIO**: For scalable storage of media files (images, videos, audio) and documents.
  - **Redis**: For response caching and pub/sub messaging.

## Database Migrations

The application now applies Entity Framework Core migrations at startup (`db.Database.Migrate()`).
For production deployments it's recommended to run migrations explicitly as part of your deployment pipeline using:

```bash
dotnet ef database update --project src/SmartGlassesMiddleware
```

This ensures predictable schema changes; automatic migration-on-startup is convenient for development and simple setups but may not be appropriate for all production workflows.

You can control migration-on-startup behavior using either the `MigrateOnStartup` configuration key (e.g., in `appsettings.json` or environment variables) or the `MIGRATE_ON_STARTUP` environment variable. Example:

```json
{
  "MigrateOnStartup": true
}
```

Or set the environment variable:

```bash
export MIGRATE_ON_STARTUP=true
```

If neither is set, automatic migration on startup is disabled.

### CI / Deployment Step

For production deployments it's recommended to apply migrations explicitly as part of your CI/CD pipeline rather than relying on automatic migration at startup. A deployment step should:

- Build the application (or ensure the runtime tooling is available).
- Run EF Migrations against the target database using the connection string for that environment, for example:

```bash
dotnet ef database update --project src/SmartGlassesMiddleware --no-build
```

- Alternatively, generate an idempotent SQL migration script and apply it with your database tooling:

```bash
dotnet ef migrations script --idempotent --project src/SmartGlassesMiddleware -o migration.sql
# then apply `migration.sql` with psql or your DBA tooling
```

Notes:
- Running `dotnet ef database update` from CI requires the `dotnet-ef` tool and access to the production database; ensure credentials and network access are handled securely.
- Generating SQL scripts and having DBAs apply them is often preferred for controlled production deployments.
- If you enable `MigrateOnStartup` (or `MIGRATE_ON_STARTUP=true`), the service will attempt to apply migrations at startup; the `/health` endpoint will report pending migrations and be marked unhealthy when migrations are pending but auto-apply is disabled.
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

# 1. Copy the example environment file
cp .env.example .env

# 2. Edit .env and set your configuration values
# At minimum, update passwords and secrets for security

# 3. Start the services
docker-compose pull
docker-compose up --build
```

The API will then be available at `http://localhost:5000`.

## Configuration

The application is now configured entirely via a `.env` file for ease of use. All configuration settings are documented in `.env.example`.

### Setting up Configuration

1. **Copy the example file**: `cp .env.example .env`
2. **Edit `.env`**: Update the values according to your environment
3. **Key settings to configure**:
   - Database credentials (`POSTGRES_PASSWORD`)
   - MinIO credentials (`MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY`)
   - AI model endpoints (`OLLAMA_ENDPOINT`, `AI_ADVANCED_ENDPOINT`)
   - Service endpoints (Redis, Transcription, etc.)
   - Security settings (API keys, CORS origins)

### Configuration Sections

The `.env` file is organized into the following sections:

- **Environment**: ASP.NET environment (Development/Production)
- **Database**: PostgreSQL connection settings
- **Redis**: Redis cache configuration
- **MinIO**: S3-compatible object storage settings
- **AI Models**: Ollama and advanced model configuration
- **Transcription**: Speech-to-text service settings
- **CORS**: Cross-origin request policies
- **Background Tasks**: Task retention and cleanup settings
- **Service Timeouts**: Timeout configuration for external services
- **Rate Limiting**: API rate limiting settings
- **Metrics**: Monitoring and metrics endpoint protection
- **RAG**: Retrieval-Augmented Generation pipeline tuning
- **Logging**: Log level configuration

For detailed descriptions of each setting, see `.env.example`.

**Persisting Data on Host (Windows)**

- To persist service data to specific host directories on Windows, set the following variables in your `.env` to the desired absolute paths (or keep named volumes):

```powershell
# Example Windows host-path bind mounts
POSTGRES_DATA_VOLUME=C:/docker-data/postgres
MINIO_DATA_VOLUME=C:/docker-data/minio
OLLAMA_DATA_VOLUME=C:/docker-data/ollama
VOSK_MODELS_VOLUME=C:/docker-data/vosk-models
```

- Notes:
  - Use forward slashes (`/`) in Windows paths when used in `docker-compose`.
  - If you prefer Docker-managed named volumes, keep the defaults (e.g., `postgres-data`).
  - Ensure Docker Desktop has filesystem access to the chosen host directories.

- Start the stack after configuring `.env`:

```powershell
# From repository root
cp .env.example .env    # or copy manually in File Explorer
# Edit .env (e.g. with Notepad or VS Code)
# Then start the services
docker-compose pull
docker-compose up -d --build
```

### Legacy Configuration Files

The application still uses `appsettings.json` for default values, but **all secrets and environment-specific settings should be in `.env`**. The environment variables from `.env` will override the appsettings.json defaults.

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

### End-to-End Testing

This repository includes comprehensive end-to-end tests using Playwright 1.58.2. For detailed information about running and writing E2E tests, see [E2E_TESTING.md](E2E_TESTING.md).

Quick start for E2E tests:
```bash
# Install dependencies
npm install

# Install Playwright browsers (Chromium is sufficient for most testing)
npx playwright install chromium

# Run all E2E tests
npm test

# Or run tests on Chromium only (faster)
npx playwright test --project=chromium
```

### Run Application
```bash
dotnet run --project src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj
```