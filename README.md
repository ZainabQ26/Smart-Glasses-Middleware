# Smart Glasses Middleware

Backend middleware and frontend for a smart glasses application. Provides a multi-modal API for AI interaction, document processing, audio transcription, and task management, deployed to AWS ECS Fargate with automatic scale-to-zero when idle.

## Stack

| Layer | Technology |
|-------|-----------|
| API | ASP.NET Core (.NET 10), C# |
| Frontend | React 18 + Vite, served from S3 via CloudFront |
| AI | AWS Bedrock (Nova / Titan Embed) or Ollama (local dev) |
| Audio transcription | Whisper Lambda (containerised, invoked via SQS) |
| Document processing | Lambda (Python), iText7, Tesseract OCR, pgvector RAG |
| Database | PostgreSQL 16 + pgvector (RDS db.t3.micro) |
| Cache / pub-sub | Redis (ElastiCache, optional) |
| Object storage | AWS S3 (prod) or MinIO (local dev) |
| Real-time | SignalR over WebSocket |
| Infrastructure | Terraform, ECS Fargate, CloudFront, ALB, Secrets Manager |
| CI/CD | GitHub Actions → Terraform apply → ECR push → ECS deploy |

## Architecture Overview

```
Browser
  └─► CloudFront (HTTPS, COOP+COEP headers)
        ├─► S3  —— static frontend (React SPA, ONNX/WASM assets)
        └─► ALB —— /api/*, /health, /hubs/*, /swagger*
              └─► ECS Fargate (ASP.NET Core, port 5000)
                    ├─► RDS PostgreSQL (pgvector)
                    ├─► ElastiCache Redis (optional)
                    ├─► S3 (media + documents)
                    ├─► Bedrock (LLM + embeddings)
                    └─► SQS → Whisper Lambda (transcription)
```

### Scale-to-zero

The ECS service scales to 0 tasks when idle (default: 15 minutes of no traffic) and wakes on demand:

1. Browser loads the React SPA from S3 (always available)
2. `WakeupGate` component polls `/health` — on first non-200, immediately `POST /wake`
3. `/wake` (CloudFront → Lambda Function URL) sets `desiredCount=1` in **< 1 second**
4. ECS task starts; ALB health checks pass in ~20 s; app is serving in **~35–45 s total**
5. The CloudWatch alarm (ALB + CloudFront 5xx metrics) provides a parallel wake signal

Cost while idle: **$0** (Fargate billing stops when `desiredCount=0`).

## Local Development

### Prerequisites

- Docker Desktop
- .NET 10 SDK
- Node.js 22
- AWS CLI v2

### Start with LocalStack

```bash
# Start LocalStack + PostgreSQL + MinIO + Redis
docker-compose -f docker-compose.localstack.yml up -d

# Verify LocalStack
curl http://localhost:4566/_localstack/health
```

### Run the API

```bash
cd src/SmartGlassesMiddleware
dotnet run
```

API available at `http://localhost:5000`. Swagger UI at `http://localhost:5000/swagger`.

`appsettings.Development.json` enables `MigrateOnStartup: true` so EF migrations run automatically against the local database on first start.

### Run the Frontend (standalone)

```bash
cd src/SmartGlassesMiddleware/client
npm install
npm run dev   # Vite dev server at http://localhost:5173
```

### Build the Docker image locally

```bash
# Must use repo root as context so tasks/ folder is included in the build
docker build -f src/SmartGlassesMiddleware/Dockerfile -t smart-glasses-api:local .
```

## Configuration

### Local dev (`appsettings.json` + `appsettings.Development.json`)

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:DefaultConnection` | `Host=localhost;...` | PostgreSQL |
| `ConnectionStrings:Redis` | `localhost:6379` | Redis (optional) |
| `MinIO:Endpoint` | `localhost:9000` | MinIO endpoint |
| `AI:OllamaEndpoint` | `http://localhost:11434` | Ollama API |
| `AI:OllamaModel` | `llama3:latest` | Chat model |
| `AI:UseBedrockAI` | `false` | Switch to Bedrock |
| `MigrateOnStartup` | `true` (dev) / `false` (AWS) | Run EF migrations on boot |

### AWS (`appsettings.AWS.json`)

Secrets (DB connection string, S3 bucket names, Redis URL) are injected at runtime from AWS Secrets Manager via ECS task secret references — no plaintext credentials in the image.

`AI:UseBedrockAI=true` switches the LLM and embedding client to Bedrock (Nova / Titan Embed V2). The ECS task role grants the necessary `bedrock:InvokeModel` permissions.

## Database Migrations

Migrations are **only applied during CI deployment**, not at container startup, to eliminate cold-start latency and migration lock risk.

**CI flow:** `terraform-apply` → `run-migrations` (one-shot Fargate task with `MIGRATE_ONLY=true`) → `verify-deployment`

The `run-migrations` CI job:
1. Launches a Fargate task using the just-deployed image with env override `MIGRATE_ONLY=true`
2. The container runs `db.Database.Migrate()` then exits cleanly (exit code 0)
3. CI waits, checks the exit code, and prints CloudWatch logs on failure

**For local development**, `MigrateOnStartup: true` in `appsettings.Development.json` applies migrations on `dotnet run`.

**Manual migration** (if needed):
```bash
dotnet ef database update --project src/SmartGlassesMiddleware
```

## Task Handlers

The middleware dispatches AI-classified requests to typed task handlers. Handlers are written in C# and implement `ITaskHandler`.

### Built-in handlers (`tasks/`)

| Handler | Task name | Description |
|---------|-----------|-------------|
| `TimeTaskHandler` | `time` | Returns current date/time |
| `ReminderTaskHandler` | `reminder` | Saves a reminder to the DB |
| `DeleteReminderTaskHandler` | `delete-reminder` | Deletes a reminder by matching text |
| `EditReminderTaskHandler` | `edit-reminder` | Edits an existing reminder |
| `DocumentSearchTaskHandler` | `document-search` | Finds text in documents, returns page/position |
| `DocumentHighlightTaskHandler` | `document-highlight` | Highlights text in a document |
| `DocumentQueryTaskHandler` | `document-query` | Semantic RAG query over uploaded documents |
| `OCRTaskHandler` | `ocr` | Runs OCR on an image or scanned PDF page |
| `OCRRetryTaskHandler` | `ocr-retry` | Retries a failed OCR job |
| `AdvancedTaskHandler` | `advanced` | Forwards to a configurable external model endpoint |
| `TemplateTaskHandler` | `template` | Starter template for new handlers |

### Adding a custom handler

1. Create a `.cs` file implementing `ITaskHandler` in the `tasks/` folder
2. It is compiled into `SmartGlassesMiddleware.dll` at `dotnet publish` time via `<Compile Include>` in the `.csproj`
3. Alternatively, drop a compiled `.dll` into a `tasks/` directory alongside the running binary for runtime hot-loading (local dev only)

## API Endpoints

Run the application and visit `/swagger` for the full interactive spec.

| Group | Endpoints |
|-------|-----------|
| AI interaction | `POST /api/ai/interact` — text/task dispatch |
| Audio | `POST /api/audio/transcribe` |
| Documents | `POST /api/documents/upload`, `POST /api/documents/query`, `GET/DELETE /api/documents/{id}` |
| Media | `POST /api/media/upload`, `GET /api/media/{id}` |
| Conversations | `GET/POST/DELETE /api/conversations` |
| History | `GET /api/history` |
| Tasks | `GET /api/tasks/{id}` — background task status |
| Admin | `GET/POST/PUT/DELETE /api/admin/files`, `/api/admin/prompts`, `/api/admin/groups` |
| Monitoring | `GET /api/monitoring/metrics` |
| Health | `GET /health` — Postgres + Redis liveness |

## CI/CD Pipeline

Every push to `main` triggers:

```
build-api ──────────┐
build-lambda ───────┼──► terraform-apply ──► run-migrations ──┐
                    │         │                                 ├──► verify-deployment
                    │         └──► deploy-frontend ────────────┘
                    │
                    └── (Docker images pushed to ECR with SHA tag)
```

- **`build-api`** — builds the React frontend (Vite) and the .NET API into a single Docker image, pushes to ECR
- **`build-lambda`** — builds the Whisper Lambda container image and the document processor Lambda ZIP
- **`terraform-apply`** — runs `terraform apply` (via Docker) with the new image SHA as a variable; manages all AWS infrastructure
- **`run-migrations`** — one-shot Fargate task applies EF Core migrations against RDS
- **`deploy-frontend`** — syncs built assets to S3, invalidates CloudFront cache
- **`verify-deployment`** — waits for ECS service stability, fetches container logs, smoke-tests `/health`

Infrastructure is defined in `aws/infrastructure/` (Terraform). Local testing uses `aws/LOCALSTACK.md`.

## Infrastructure Notes

- **CloudFront → ALB protocol**: HTTP (not HTTPS) for the origin connection. The ALB TLS certificate covers the custom domain only; CloudFront validates against the ELB hostname which doesn't match, causing 502s with `https-only`.
- **COOP + COEP headers**: Set by a CloudFront response headers policy to enable `SharedArrayBuffer` in the browser, required for `@huggingface/transformers` v3 (ONNX Runtime threaded WASM).
- **ECS CPU**: 1 vCPU (1024 units) to reduce .NET 10 JIT cold-start time. Cost impact is negligible with scale-to-zero.
- **ALB health check interval**: 10 s, `healthy_threshold=2` — ALB marks the target healthy ~20 s after the container is listening.

