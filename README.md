# Smart-Glasses-Middleware

This repository contains the backend middleware for a smart glasses application. It provides a powerful, multi-modal API for interacting with AI models, processing documents, and managing tasks. The system is designed to be extensible, allowing developers to add new capabilities with ease.

## Deployment with AWS LocalStack

This application is designed to deploy to AWS using LocalStack for local development and testing before deploying to production AWS environments.

### What is LocalStack?

LocalStack is a fully functional local AWS cloud stack that allows you to develop and test cloud applications offline. It provides a testing environment for AWS services including S3, Lambda, Secrets Manager, RDS, ElastiCache, and more.

### Prerequisites

- Docker and Docker Compose installed
- AWS CLI v2 installed
- .NET 8.0 SDK (or later)
- VS Code with AWS Toolkit extension (optional but recommended)

### Quick Start with LocalStack

1. **Start LocalStack with all required services:**

```bash
docker-compose -f docker-compose.localstack.yml up -d
```

2. **Verify LocalStack is running:**

```bash
# Check LocalStack health
curl http://localhost:4566/_localstack/health

# List S3 buckets (should see 3 buckets created automatically)
aws --endpoint-url=http://localhost:4566 s3 ls
```

3. **Access the application:**

The API will be available at `http://localhost:5000` after the containers start.

### VS Code AWS Toolkit Setup

For the best development experience with LocalStack, configure VS Code AWS Toolkit:

1. **Create a LocalStack AWS profile:**

PowerShell (Windows):
```powershell
aws configure set aws_access_key_id test --profile localstack
aws configure set aws_secret_access_key test --profile localstack
aws configure set region us-east-1 --profile localstack
```

Bash (Linux/Mac):
```bash
aws configure set aws_access_key_id test --profile localstack
aws configure set aws_secret_access_key test --profile localstack
aws configure set region us-east-1 --profile localstack
```

2. **Connect the AWS Toolkit to the LocalStack profile:**
   - Open VS Code Command Palette (Ctrl+Shift+P / Cmd+Shift+P)
   - Run `AWS: Add Connection`
   - Choose `Shared credentials` and select the `localstack` profile

3. **Use the LocalStack endpoint for CLI operations:**

PowerShell:
```powershell
$env:AWS_PROFILE = "localstack"
$env:AWS_ENDPOINT_URL = "http://localhost:4566"
aws s3 ls --endpoint-url http://localhost:4566
```

Bash:
```bash
export AWS_PROFILE=localstack
export AWS_ENDPOINT_URL=http://localhost:4566
aws s3 ls --endpoint-url http://localhost:4566
```

📚 **For detailed LocalStack testing and deployment information, see:** [aws/LOCALSTACK.md](aws/LOCALSTACK.md)

## Features

- **Multi-format Document Processing**: Extracts text and layout information from PDFs (digital and scanned), DOCX, TXT, and various image formats.
- **Vector Embeddings**: Generates embeddings for document chunks using configurable models and stores them in PostgreSQL with `pgvector` for efficient similarity searches.
- **Semantic Querying**: Understands the meaning behind your questions to find the most relevant information.
- **Document Highlighting**: Pinpoints and highlights the exact location of information within your documents.
- **Resource Groups**: Organize documents and prompts into groups for targeted querying.
- **PostgreSQL**: For storing request history, document metadata, and vector embeddings.
- **S3-Compatible Storage**: For scalable storage of media files (images, videos, audio) and documents (using AWS S3 or LocalStack S3).
- **Redis**: For response caching and pub/sub messaging.

## Architecture

The application is built on a modern .NET 8 stack, with a focus on modularity and scalability.

- **ASP.NET Core Web API**: Provides the core framework for the RESTful API.
- **Microsoft Semantic Kernel**: For AI orchestration, prompt engineering, and service connections.
- **AWS Bedrock**: AI model services (or LocalStack mock for local testing).
- **PostgreSQL + pgvector**: For relational data storage and efficient vector similarity search.
- **AWS S3 / LocalStack S3**: S3-compatible object storage for all media and document files.
- **Redis**: For caching, background task management, and real-time messaging.
- **SignalR**: For pushing real-time notifications to connected clients.

## Database Migrations

The application applies Entity Framework Core migrations at startup when `MigrateOnStartup` is enabled.

For production deployments, it's recommended to run migrations explicitly:

```bash
dotnet ef database update --project src/SmartGlassesMiddleware
```

You can control migration-on-startup behavior using the `MigrateOnStartup` configuration key in `appsettings.json` or the `MIGRATE_ON_STARTUP` environment variable.

## Configuration

The application uses environment variables for configuration. See `.env.example` for all available options.

### Key Configuration for LocalStack

When running with LocalStack, the following environment variables are automatically set by docker-compose.localstack.yml:

- `AWS_REGION=us-east-1`
- `AWS_ACCESS_KEY_ID=test`
- `AWS_SECRET_ACCESS_KEY=test`
- `AWS_ENDPOINT_URL=http://localstack:4566`
- `AWS__UseLocalStack=true`
- `AWS__S3__Endpoint=http://localstack:4566`
- `AWS__Bedrock__Endpoint=http://localstack:4566`

## API Endpoints

The application exposes a comprehensive set of RESTful API endpoints. For a complete and interactive API specification, run the application and navigate to `/swagger`.

### Main Endpoints

- `POST /api/text`: Submit a text-based query
- `POST /api/transcribe`: Submit an audio file for transcription and processing
- `POST /api/ask`: Submit a media file (image/video) with a prompt
- `GET /api/history`: Retrieve request history

### Document (RAG) Endpoints

- `POST /api/documents/upload`: Upload a document for processing
- `POST /api/documents/query`: Query your documents
- `GET /api/documents`: List uploaded documents
- `GET /api/documents/{id}`: Get details for a specific document

### Admin Endpoints

- `GET, POST, PUT, DELETE /api/admin/files`: Manage uploaded files
- `GET, POST, PUT, DELETE /api/admin/prompts`: Manage saved prompts
- `GET, POST, PUT, DELETE /api/admin/groups`: Manage resource groups

### Task Management

- `GET /api/tasks/{id}`: Get the status of a background task

## Task Handlers

The middleware can be extended with custom task handlers. These are C# classes that implement the `ITaskHandler` interface. The system automatically discovers and registers any `ITaskHandler` implementations found in `.cs` or `.dll` files in the `tasks` directory at startup.

### Creating a Task Handler

1. Create a new C# class that implements `SmartGlassesMiddleware.Services.ITaskHandler`
2. Implement the `CanHandle` method to specify which tasks your handler can process
3. Implement the `HandleAsync` method to perform the task's logic
4. Place the `.cs` file (or compiled `.dll`) in the `tasks` directory

The application will automatically pick up your new handler on the next restart. See the existing handlers in the `tasks` directory for examples.

## Development

### Prerequisites

- .NET 8.0 SDK (or later)
- Docker (for LocalStack and dependencies)
- AWS CLI v2

### Build

```bash
dotnet build
```

### Run Tests

```bash
dotnet test
```

### Run Application Locally

```bash
# Start LocalStack
docker-compose -f docker-compose.localstack.yml up -d

# Run the application
dotnet run --project src/SmartGlassesMiddleware/SmartGlassesMiddleware.csproj
```

## Production Deployment to AWS

For production deployment to AWS (not LocalStack), you'll need to:

1. Set up AWS infrastructure (VPC, RDS, ElastiCache, S3, ECR, ECS/Lambda)
2. Configure production environment variables
3. Deploy the application container to ECS Fargate or Lambda
4. Configure proper security groups, IAM roles, and secrets management

See [aws/LOCALSTACK.md](aws/LOCALSTACK.md) for testing with LocalStack before deploying to production.

## Support

For issues or questions:
- Check LocalStack logs: `docker-compose -f docker-compose.localstack.yml logs`
- Review AWS CLI configuration
- Verify network connectivity
- Consult LocalStack documentation: https://docs.localstack.cloud/
