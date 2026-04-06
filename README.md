# Smart Glasses Middleware

## Overview

Backend middleware and frontend for a smart glasses application. Provides a multi-modal API for AI interaction, document processing, audio transcription, and task management. It operates as an always-online, single-server architecture deployed on AWS ECS Fargate, utilizing a pure in-memory state serialized to S3/MinIO.

## Stack & Architecture

| Layer | Technology |
|-------|-----------|
| API | ASP.NET Core (.NET 10), C# |
| Frontend | React 19 + Vite, served statically by ASP.NET Core from `wwwroot` |
| AI | AWS Bedrock (Nova / Titan Embed) — always |
| Advanced model | External REST API (`AI:AdvancedModelEndpoint`) — request/response |
| Audio transcription | In-browser Whisper WebGPU worker (`Xenova/whisper-tiny`) |
| Document processing | Lambda (Python), iText7, Tesseract OCR |
| Database | Pure `InMemoryDatabase` with `StatePersistenceService` serializing to S3/MinIO |
| Object storage | AWS S3 (prod) or MinIO (local dev) |
| Real-time | Continuous polling |
| Infrastructure | Terraform, ECS Fargate, CloudFront, ALB, Secrets Manager |
| CI/CD | GitHub Actions → Terraform apply → ECR push → ECS deploy |

### Architecture Principles

- **Single Always-Online Server:** The application is permanently online and does not scale to zero. It runs as a single instance (`ecs_api_desired_count=1`, `ecs_api_min_capacity=1`, `ecs_api_max_capacity=1`) leveraging Fargate Spot for cost optimization. Wakeup overlays have been removed.
- **In-Memory State & Storage:** External PostgreSQL and Redis dependencies have been completely removed. State is loaded and persisted using `StatePersistenceService` to S3 (production) or MinIO (local development) during boot and shutdown. RAG and pgvector dependencies have been removed.
- **Polling over WebSockets:** The application relies entirely on continuous polling for state updates in the React frontend, avoiding SignalR and cross-tab storage events.
- **In-Browser Transcription:** The frontend uses `@huggingface/transformers` to run the `Xenova/whisper-tiny` model directly in the browser via WebGPU acceleration, preserving memory on constrained mobile environments via quantized model weights.
- **Static Frontend Serving:** The Vite React frontend compiles directly to the ASP.NET Core `wwwroot` directory and is served by the backend, simplifying deployment.
- **Always Free Tier AWS:** Infrastructure is designed to stay within the AWS Free Tier, relying on static Tesseract binaries in Lambda rather than Amazon Textract.

## CI/CD Pipeline

Deployments are managed via GitHub Actions:

- **`deploy-main.yml`** — full deploy (Terraform + frontend), triggers on `v*` tags or `workflow_dispatch`
- **`deploy-fast.yml`** — fast deploy (no Terraform), triggers on `deploy-*` tags or `workflow_dispatch`

Tag-based deploy examples:
```bash
# Full deploy (Terraform + infrastructure):
git tag v$(date +%Y%m%d-%H%M)
git push origin --tags

# Fast deploy (skips Terraform, updates image):
git tag deploy-$(date +%Y%m%d-%H%M)
git push origin --tags
```

## Local Development

### Prerequisites

- Docker Desktop
- .NET 10 SDK
- Node.js 25.8.1
- AWS CLI v2

### Start Storage Services

Although the codebase includes a full `docker-compose.localstack.yml`, the application primarily relies on MinIO/LocalStack for S3 API compatibility as Postgres and Redis have been removed.

```bash
docker-compose -f docker-compose.localstack.yml up -d
```

### Build Frontend

The frontend is a React application built with Vite that outputs static files to `../wwwroot` for ASP.NET Core to serve.

```bash
cd src/SmartGlassesMiddleware/client
npm install
npm run build
```

*Note: For live preview development, run `npm run dev -- --host 0.0.0.0 --port 3000` with `__VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS=.com` to allow proxy routing.*

### Run the API

```bash
cd src/SmartGlassesMiddleware
dotnet run
```

API available at `http://localhost:5000`. Swagger UI at `http://localhost:5000/swagger`.

## Administration & Handlers

### Admin Panel

The admin panel's 'Conversation' tab functions as a single, real-time chat interface. Historical message persistence has been removed. Abbreviations are no longer stored in a database; they are loaded and maintained purely in-memory via `AbbreviationService`.

### Task Handlers

The middleware dispatches AI-classified requests to typed task handlers implementing `ITaskHandler`. Place new core handlers in `src/SmartGlassesMiddleware/Services/` under the `SmartGlassesMiddleware.Services` namespace. The `tasks/` directory is reserved for local dev plugin drops.

## Documentation Site (DocFX + GitHub Pages)

- Static docs are built by DocFX from `docs/` and published via `.github/workflows/docs-pages.yml` to GitHub Pages.
- C# API reference is generated from source code standard XML doc comments.
- Test coverage reports are automatically included as a resource in the generated site.

To build locally:
```bash
dotnet tool install -g docfx
./scripts/run_tests.sh
cp -r CoverageReport/* docs/tests/
docfx docs/docfx.json
```

## Unit Testing

A comprehensive automated test suite utilizes `NUnit` and `Moq`. The project uses `System.TimeProvider` for time abstraction to enable deterministic testing.

### Running tests
```bash
./scripts/run_tests.sh
```
This script runs all unit tests, generates code coverage reports (HTML/Text/PDF), and enforces a minimum coverage threshold of 10%. Tests evaluate various backend components including services, task handlers, and controllers utilizing mock data.

*Note: Keep test files in the base `SmartGlassesMiddleware.Tests` namespace to avoid shadowing issues.*
