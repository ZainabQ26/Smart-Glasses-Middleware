# Smart Glasses Middleware

## Deploy Via GitHub Actions (Tag + Manual Run)

There are two deploy workflows:

- **`deploy-main.yml`** — full deploy (Terraform + migrations + frontend), triggers on `v*` tags or `workflow_dispatch`
- **`deploy-fast.yml`** — fast deploy (no Terraform), triggers on `deploy-*` tags or `workflow_dispatch`

1. Tag-based deploy (push a tag to trigger Actions):

```bash
# From repo root, on the commit you want to deploy
git pull

# Full deploy (Terraform + migrations):
git tag v$(date +%Y%m%d-%H%M)
git push origin --tags

# Fast deploy (skips Terraform, just builds and deploys the new image):
git tag deploy-$(date +%Y%m%d-%H%M)
git push origin --tags
```

The workflow triggers on tags matching:

- `v*` — full deploy via `deploy-main.yml`
- `deploy-*` — fast deploy via `deploy-fast.yml`

## Documentation Site (DocFX + GitHub Pages)

This repo includes an automated docs workflow in `.github/workflows/docs-pages.yml`.

- Static docs are built by DocFX from `docs/`
- C# API reference is generated from `src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj`
- Runs manually via `workflow_dispatch` and publishes to GitHub Pages
- Documentation URL: https://robertbiv.github.io/Smart-Glasses-Middleware/

### One-time GitHub setup

1. In GitHub, open **Settings -> Pages**
2. Under **Build and deployment**, set **Source** to **GitHub Actions**

### Manual run

```bash
gh workflow run docs-pages.yml --ref main
```

### Monitor run

```bash
gh run list --workflow docs-pages.yml --limit 5
gh run watch <run-id> --exit-status
```

2. Manual deploy (`workflow_dispatch`) with required inputs:

```bash
# Dev deploy, recommended defaults
gh workflow run deploy-main.yml \
      --ref main \
      -f environment=dev \
      -f skip_migrations=true \
      -f force_recreate_dev=false
```

Input guidance:

- `environment`: `dev|staging|prod`
- `skip_migrations`: set `true` for fresh dev setups
- `force_recreate_dev`: keep `false` unless you intentionally want destructive cleanup of dev resources

3. Watch deploy progress:

```bash
gh run list --workflow deploy-main.yml --limit 5
gh run watch <run-id> --exit-status
```

After deploy completes, the workflow logs now print a notice line with the live site URL:

- `Deployment URL: https://...`

Current dev URL:

- `https://d2vzlm78lj63v4.cloudfront.net`

In GitHub Actions, open the `verify-deployment` job and check the `Print deployed website URL` step.

Backend middleware and frontend for a smart glasses application. Provides a multi-modal API for AI interaction, document processing, audio transcription, and task management, deployed to AWS ECS Fargate with automatic scale-to-zero when idle.

## Stack

| Layer | Technology |
|-------|-----------|
| API | ASP.NET Core (.NET 10), C# |
| Frontend | React 19 + Vite, served from S3 via CloudFront |
| AI | AWS Bedrock (Nova / Titan Embed) — always |
| Advanced model | External REST API (`AI:AdvancedModelEndpoint`) — request/response |
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
                    ├─► Advanced model REST API (external, optional)
                    └─► SQS → Whisper Lambda (transcription)
```

### Scale-to-zero

The ECS service scales to 0 tasks when idle (default: 15 minutes of no traffic) and wakes on demand:

1. Browser loads the React SPA from S3 (always available)
2. `WakeupGate` component polls `/health` — on first non-200, immediately `POST /wake`
3. `/wake` (CloudFront → Lambda Function URL) sets `desiredCount=1` in **< 1 second** and requests `StartDBInstance` if the dev DB is stopped
4. ECS task starts; ALB health checks pass in ~20 s; app is serving in **~35–45 s total**
5. The CloudWatch alarm (ALB + CloudFront 5xx metrics) provides a parallel wake signal

Cost while idle: **$0** (Fargate billing stops when `desiredCount=0`).

## Local Development

### Prerequisites

- Docker Desktop
- .NET 10 SDK
- Node.js 25.8.1
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

Note: `src/SmartGlassesMiddleware/wwwroot/index.html` and `src/SmartGlassesMiddleware/wwwroot/assets/` are build-generated by Vite (`npm run build`) and are intentionally not tracked in git. The GitHub Actions `deploy-frontend` job builds these files and uploads them to S3/CloudFront.

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
| `MinIO:Endpoint` | `localhost:9000` | MinIO endpoint (local storage) |
| `AI:AdvancedModelEndpoint` | `` | External REST API for unknown tasks |
| `AI:UseAdvancedModel` | `false` | Enable advanced model routing |
| `AI:ActivationWord` | `Iris` | Wake word that triggers command mode |
| `AWS:Bedrock:ModelId` | `us.amazon.nova-2-lite-v1:0` | Bedrock chat model |
| `AWS:Bedrock:EmbeddingModelId` | `amazon.titan-embed-text-v2:0` | Bedrock embedding model |
| `MigrateOnStartup` | `true` (dev) / `false` (AWS) | Run EF migrations on boot |

### Speech Transcription Mode

The client-side transcription behavior is configurable via the `AI:SpeechTranscriptionMode` setting. Add this under the `AI` section in your `appsettings.*.json` files or provide it as an environment variable (`AI__SpeechTranscriptionMode`). Valid values:

- `whisper-only`: Ignore Web Speech results and always use the on-device Whisper WebGPU worker for transcription (default).
- `auto`: Use the browser Web Speech API as the primary fast-path; fall back to the Whisper worker when needed.
- `webspeech-only`: Use only the browser Web Speech API. The Whisper worker is ignored.

The current client implementation uses only the local Whisper WebGPU worker (single multilingual `whisper-base` model running in a Web Worker with WebGPU acceleration). The `SpeechTranscriptionMode` setting is read by the server and exposed via `GET /api/process/client-settings`.

The server exposes a convenience endpoint that the SPA calls on load:

- `GET /api/process/client-settings` — returns `{ "speechTranscriptionMode": "whisper-only|auto|webspeech-only", "activationWord": "Iris" }`.

### AWS (`appsettings.AWS.json`)

Secrets (DB connection string, S3 bucket names, Redis URL) are injected at runtime from AWS Secrets Manager via ECS task secret references — no plaintext credentials in the image.

AI classification and embeddings always use Bedrock (Nova / Titan Embed V2). The ECS task role grants the necessary `bedrock:InvokeModel` permissions. Unknown tasks are forwarded to the advanced model REST API (`AI:AdvancedModelEndpoint`).

## Advanced Model

When `AI:UseAdvancedModel=true`, requests that don't match a local task handler are forwarded to the external REST API configured at `AI:AdvancedModelEndpoint`.

The request is a simple JSON POST:
```json
{ "message": "user input" }
```

The response is expected to contain a `response` field:
```json
{ "response": "AI response text" }
```

If the response cannot be parsed, the raw body is returned.

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
| `ShowRemindersTaskHandler` | `show-reminders` | Lists all saved reminders |
| `SelectDocumentTaskHandler` | `select-document` | Opens the most recently uploaded document in the inline viewer |
| `OpenFileTaskHandler` | `open-file` | Lists the last 5 uploaded files so the user can choose one to open |
| `DocumentSearchTaskHandler` | `document-search` | Finds text in documents, returns page/position |
| `DocumentHighlightTaskHandler` | `document-highlight` | Highlights text in a document |
| `AdvancedTaskHandler` | `advanced` | Forwards to the configured external REST API endpoint |
| `TemplateTaskHandler` | `template` | Starter template for new handlers |
| `NextPageTaskHandler` | `next-page` | Navigates the inline PDF viewer to the next page |
| `PreviousPageTaskHandler` | `previous-page` | Navigates the inline PDF viewer to the previous page |
| `HighlightWordTaskHandler` | `highlight-word` | Highlights a word/phrase in the open PDF; cycles color on repeat |
| `RemoveHighlightsTaskHandler` | `remove-highlights` | Clears all keyword highlights from the open PDF |
| `CloseDocumentTaskHandler` | `close-document` | Closes the inline PDF viewer and returns to the home screen |
| `GoToPageTaskHandler` | `go-to-page` | Navigates the inline PDF viewer to a specific page number |
| `GoToLoincPageTaskHandler` | `go-to-loinc` | Searches for a LOINC code in the open PDF, navigates to its page, and highlights it |
| `SummarizePageTaskHandler` | `summarize-page` | Summarises the text on the currently visible PDF page via Bedrock |
| `PrintTaskHandler` | `print` | Triggers the browser print dialog for the open PDF |
| `PrintHighlightedPagesTaskHandler` | `print-highlighted-pages` | Prints only the pages that contain at least one active highlight |
| `AdminPanelTaskHandler` | `open-admin-panel` | Navigates the frontend to the admin panel |

### Adding a custom handler

1. Create a `.cs` file implementing `ITaskHandler` in the `tasks/` folder
2. It is compiled into `SmartGlassesMiddleware.dll` at `dotnet publish` time via `<Compile Include>` in the `.csproj`
3. Alternatively, drop a compiled `.dll` into a `tasks/` directory alongside the running binary for runtime hot-loading (local dev only)

## Inline PDF Viewer

The home screen includes a built-in PDF viewer powered by [PDF.js](https://mozilla.github.io/pdf.js/).  The viewer is **hidden by default** and appears as a full-screen overlay only when a document is opened.  Opening a different document while one is already open automatically closes the old document and starts fresh — highlights do **not** carry over between documents.

The **activation word** (default: `Iris`, configurable via `AI:ActivationWord`) wakes the assistant for each command. All voice commands below assume the activation word has just been spoken.

### Opening a document

Say the **activation word** and then one of:

| Voice command | What happens |
|--------------|--------------|
| `"open my files"` / `"show recent files"` | Lists the last 5 uploaded files; say a number to open one |
| `"open the last document"` / `"select document"` | Opens the most recently uploaded document |

### Navigating pages

| Voice command | Action |
|--------------|--------|
| `"next page"` | Advance to the next page |
| `"previous page"` / `"go back"` | Return to the previous page |
| `"go to page 5"` / `"jump to page 12"` | Jump directly to page 5 / 12 |

Highlights are preserved when changing pages — they carry over to every page in the document and the legend always shows where each keyword was found.

### Navigating by LOINC code

| Voice command | Action |
|--------------|--------|
| `"go to LOINC code 8480-6"` | Searches the document for LOINC code `8480-6`, jumps to the first matching page, and highlights the code |
| `"find LOINC 1558-6"` | Same, for code `1558-6` |
| `"show me LOINC 8480 dash 6"` | Spoken form also accepted |

### Highlighting keywords

| Voice command | Action |
|--------------|--------|
| `"highlight <word>"` | Highlights every occurrence of `<word>` in yellow |
| `"highlight <word>"` *(said again)* | Re-highlights in the next color (sky blue → green → coral → lavender → grey, cycling) |
| `"remove highlights"` / `"clear highlights"` | Removes all keyword highlights |

Highlighting supports multi-word phrases (e.g. `"highlight patient information"`).

A **Highlights legend** is displayed below the PDF canvas.  It shows each highlighted keyword alongside its color and the sorted list of page numbers on which it was found.  The legend updates automatically when highlights are added or removed.

### Closing the viewer

| Voice command | Action |
|--------------|--------|
| `"close document"` | Hides the PDF viewer and returns to the home screen |

The ✕ **Close** button in the viewer header also closes it.

### Summarising the current page

| Voice command | Action |
|--------------|--------|
| `"summarize page"` / `"summarize this page"` | Sends the current page text to Bedrock and displays a summary |
| `"what is on this page"` | Same |

### Printing

| Voice command | Action |
|--------------|--------|
| `"print document"` / `"print pdf"` | Opens the browser print dialog for the full document |
| `"print highlighted pages"` / `"print pages with highlights"` | Prints only pages that contain at least one active highlight |

### Reminders

| Voice command | Action |
|--------------|--------|
| `"show reminders"` / `"my reminders"` | Displays a list of all saved reminders |

### Admin panel

| Voice command | Action |
|--------------|--------|
| `"open admin panel"` / `"go to admin"` | Navigates the frontend to the admin panel |

## API Endpoints

Run the application and visit `/swagger` for the full interactive spec (powered by [Scalar](https://scalar.com)).
The raw OpenAPI JSON is available at `/openapi/v1.json`.

| Group | Endpoints |
|-------|-----------|
| AI interaction | `POST /api/ask` — text/task dispatch |
| Process | `POST /api/process` — voice transcript → AI task dispatch; `GET /api/process/client-settings` |
| Documents | `POST /api/documents/upload`, `POST /api/documents/query`, `GET/DELETE /api/documents/{id}` |
| Conversations | `GET/POST/PUT/DELETE /api/conversations` |
| History | `GET /api/history`, `GET/PUT/DELETE /api/history/reminders/{id}` |
| Tasks | `GET /api/tasks/{id}` — background task status |
| Admin | `GET/POST/PUT/DELETE /api/admin/files`, `/api/admin/prompts`, `/api/admin/groups` |
| Monitoring | `GET /api/monitoring/requests` |
| AI Interactions | `GET /api/ai-interactions` — model usage logs and stats |
| Health | `GET /health` — Postgres + Redis liveness |
| Debug | `GET /api/debug/tasks/status` — handler registration diagnostics |

## CI/CD Pipeline

Deployments are tag-driven (see [Deploy Via GitHub Actions](#deploy-via-github-actions-tag--manual-run) above). Pushing a `v*` tag (full deploy) or a `deploy-*` tag (fast deploy, no Terraform) triggers the pipeline:

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

Infrastructure is defined in `aws/infrastructure/` (Terraform). Local testing uses `docs/LOCALSTACK.md`.

## Unit Testing

A comprehensive automated test suite has been added under the `tests/` directory.

### Running tests
```bash
dotnet test tests/SmartGlassesMiddleware.Tests/SmartGlassesMiddleware.Tests.csproj
```

### Coverage report
```bash
dotnet test tests/SmartGlassesMiddleware.Tests/SmartGlassesMiddleware.Tests.csproj --collect:"XPlat Code Coverage"
```

The `tests/` directory contains tests utilizing `NUnit` and `Moq`. These tests evaluate various backend components including services, task handlers, and controllers, with tests that span end-to-end component flows utilizing mock data to isolate logic successfully without external dependencies like the Database. For more info, please see `tests/README.md`.

## Old Readmes

Additional documentation (like AWS details, abbreviations sync instructions, advanced API calls, and localstack specifics) are kept in the `docs/` folder for reference.

## Infrastructure Notes

- **CloudFront → ALB protocol**: HTTP (not HTTPS) for the origin connection. The ALB TLS certificate covers the custom domain only; CloudFront validates against the ELB hostname which doesn't match, causing 502s with `https-only`.
- **COOP + COEP headers**: Set by a CloudFront response headers policy to enable `SharedArrayBuffer` in the browser, required for `@huggingface/transformers` v3 (ONNX Runtime threaded WASM).
- **ECS CPU**: 1 vCPU (1024 units) to reduce .NET 10 JIT cold-start time. Cost impact is negligible with scale-to-zero.
- **ALB health check interval**: 10 s, `healthy_threshold=2` — ALB marks the target healthy ~20 s after the container is listening.

