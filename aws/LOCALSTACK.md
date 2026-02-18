# LocalStack Testing Guide

This guide explains how to test AWS services locally using LocalStack.

## What is LocalStack?

LocalStack is a fully functional local AWS cloud stack that allows you to develop and test cloud applications offline. It provides a testing environment for:

- S3 (object storage)
- Lambda (serverless functions)
- DynamoDB (NoSQL database)
- Secrets Manager (credential management)
- CloudWatch Logs (logging)
- And many more AWS services

## Prerequisites

- Docker and Docker Compose installed
- AWS CLI installed
- .NET 10 SDK installed
- Python 3.12 (for Lambda functions)

## Quick Start

### 1. Start LocalStack

```bash
# Start LocalStack with all required services
docker-compose -f docker-compose.localstack.yml up -d

# Wait for LocalStack to be ready (check logs)
docker-compose -f docker-compose.localstack.yml logs -f localstack
```

### 2. Configure Environment

```bash
# Set environment variables for AWS CLI
export AWS_ACCESS_KEY_ID=test
export AWS_SECRET_ACCESS_KEY=test
export AWS_DEFAULT_REGION=us-east-1
export AWS_ENDPOINT_URL=http://localhost:4566
```

### 3. Verify Setup

```bash
# List S3 buckets (should see 3 buckets created by init)
aws --endpoint-url=http://localhost:4566 s3 ls

# Expected output:
# smart-glasses-dev-media
# smart-glasses-dev-documents
# smart-glasses-dev-frontend
```

## Running Tests

### Unit Tests

```bash
# Run all unit tests with LocalStack
dotnet test

# Run specific test class
dotnet test --filter "ClassName~S3MediaStorageServiceTests"

# Run with verbose output
dotnet test -v detailed
```

### E2E Tests

```bash
# Install E2E test dependencies
npm install

# Install Playwright browsers
npx playwright install chromium

# Run E2E tests
npm test

# Run specific test file
npx playwright test e2e-tests/api.spec.ts

# Run in headed mode (see browser)
npm run test:headed

# Debug mode
npm run test:debug
```

## LocalStack Service Configuration

### S3 (Object Storage)

LocalStack automatically creates these buckets on startup:
- `smart-glasses-dev-media`: For media files (images, videos, audio)
- `smart-glasses-dev-documents`: For document files (PDFs, DOCX)
- `smart-glasses-dev-frontend`: For static frontend files

#### Testing S3 Operations

```bash
# Upload a file
aws --endpoint-url=http://localhost:4566 s3 cp test.pdf s3://smart-glasses-dev-documents/

# Download a file
aws --endpoint-url=http://localhost:4566 s3 cp s3://smart-glasses-dev-documents/test.pdf ./downloaded.pdf

# List objects
aws --endpoint-url=http://localhost:4566 s3 ls s3://smart-glasses-dev-documents/
```

### Secrets Manager

LocalStack creates these secrets on startup:
- `smart-glasses-dev-db-credentials`: Database connection info
- `smart-glasses-dev-redis-credentials`: Redis connection info
- `smart-glasses-dev-app-secrets`: Application secrets

#### Testing Secrets

```bash
# Get a secret
aws --endpoint-url=http://localhost:4566 secretsmanager get-secret-value \
  --secret-id smart-glasses-dev-db-credentials

# Create a new secret
aws --endpoint-url=http://localhost:4566 secretsmanager create-secret \
  --name test-secret \
  --secret-string '{"key":"value"}'
```

### Lambda Functions

LocalStack supports Lambda but with limitations:
- Python, Node.js, and Go runtimes supported
- Docker executor for full compatibility
- Limited Bedrock/Textract support (mocked)

#### Testing Lambda

```bash
# Deploy Lambda function
cd aws/lambda-document-processor
./build.sh

aws --endpoint-url=http://localhost:4566 lambda create-function \
  --function-name document-processor \
  --runtime python3.12 \
  --handler index.handler \
  --zip-file fileb://deployment.zip \
  --role arn:aws:iam::000000000000:role/lambda-role

# Invoke Lambda
aws --endpoint-url=http://localhost:4566 lambda invoke \
  --function-name document-processor \
  --payload '{"test":"data"}' \
  response.json
```

## Application Configuration for LocalStack

### appsettings.LocalStack.json

Create this file for LocalStack-specific settings:

```json
{
  "AWS": {
    "UseLocalStack": true,
    "Region": "us-east-1",
    "Endpoint": "http://localhost:4566",
    "S3": {
      "MediaBucket": "smart-glasses-dev-media",
      "DocumentsBucket": "smart-glasses-dev-documents",
      "Endpoint": "http://localhost:4566"
    },
    "Bedrock": {
      "ModelId": "anthropic.claude-v2",
      "EmbeddingModelId": "amazon.titan-embed-text-v1",
      "Endpoint": "http://localhost:4566"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=smartglasses;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  }
}
```

### Running the API with LocalStack

```bash
# Set environment to use LocalStack settings
export ASPNETCORE_ENVIRONMENT=LocalStack

# Or use dotnet user-secrets
dotnet user-secrets set "AWS:UseLocalStack" "true"
dotnet user-secrets set "AWS:Endpoint" "http://localhost:4566"

# Run the application
cd src/SmartGlassesMiddleware
dotnet run
```

## Writing Tests for AWS Services

### Example: S3 Storage Service Test

```csharp
using Xunit;
using Amazon.S3;
using SmartGlassesMiddleware.Services;

public class S3MediaStorageServiceTests : IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3MediaStorageService _service;
    
    public S3MediaStorageServiceTests()
    {
        // Configure for LocalStack
        var config = new AmazonS3Config
        {
            ServiceURL = "http://localhost:4566",
            ForcePathStyle = true,
            UseHttp = true
        };
        
        var credentials = new BasicAWSCredentials("test", "test");
        _s3Client = new AmazonS3Client(credentials, config);
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AWS:S3:MediaBucket"] = "smart-glasses-dev-media",
                ["AWS:S3:DocumentsBucket"] = "smart-glasses-dev-documents"
            })
            .Build();
        
        _service = new S3MediaStorageService(_s3Client, configuration, Mock.Of<ILogger<S3MediaStorageService>>());
    }
    
    [Fact]
    public async Task UploadMediaAsync_UploadsFileToS3()
    {
        // Arrange
        var fileContent = "Test file content";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));
        
        // Act
        var key = await _service.UploadMediaAsync(stream, "test.txt", "text/plain");
        
        // Assert
        Assert.NotNull(key);
        Assert.StartsWith("media/", key);
        
        // Verify file exists in S3
        var response = await _s3Client.GetObjectAsync("smart-glasses-dev-media", key);
        Assert.NotNull(response);
    }
    
    public void Dispose()
    {
        _s3Client?.Dispose();
    }
}
```

### Example: Bedrock AI Service Test

```csharp
using Xunit;
using Amazon.BedrockRuntime;
using SmartGlassesMiddleware.Services;

public class BedrockAIServiceTests
{
    [Fact]
    public async Task ProcessRequestAsync_ReturnsResponse()
    {
        // Note: Bedrock is mocked in LocalStack
        // Real tests require actual AWS or custom mocking
        
        // Arrange
        var mockBedrock = new Mock<IAmazonBedrockRuntime>();
        // Set up mock responses...
        
        var service = new BedrockAIService(
            mockBedrock.Object,
            Mock.Of<IConfiguration>(),
            Mock.Of<ILogger<BedrockAIService>>(),
            Mock.Of<ITaskDispatcher>());
        
        // Act
        var (task, response, usedAdvanced) = await service.ProcessRequestAsync("What time is it?");
        
        // Assert
        Assert.NotNull(response);
        Assert.True(usedAdvanced);
    }
}
```

## Troubleshooting

### LocalStack Not Starting

```bash
# Check Docker logs
docker-compose -f docker-compose.localstack.yml logs localstack

# Restart LocalStack
docker-compose -f docker-compose.localstack.yml restart localstack

# Clean restart
docker-compose -f docker-compose.localstack.yml down -v
docker-compose -f docker-compose.localstack.yml up -d
```

### Services Not Accessible

```bash
# Verify LocalStack is running
curl http://localhost:4566/_localstack/health

# Check if services are ready
aws --endpoint-url=http://localhost:4566 s3 ls

# Test network connectivity
docker exec localstack curl http://localhost:4566/_localstack/health
```

### Database Connection Issues

```bash
# Verify PostgreSQL is running
docker-compose -f docker-compose.localstack.yml ps postgres

# Test connection
psql -h localhost -U postgres -d smartglasses -c "SELECT 1;"

# Check logs
docker-compose -f docker-compose.localstack.yml logs postgres
```

### Redis Connection Issues

```bash
# Verify Redis is running
docker-compose -f docker-compose.localstack.yml ps redis

# Test connection
redis-cli -h localhost ping

# Check logs
docker-compose -f docker-compose.localstack.yml logs redis
```

## Limitations

### LocalStack Limitations

1. **Bedrock**: Not fully supported, requires mocking
2. **Transcribe**: Not fully supported, requires mocking
3. **Textract**: Not fully supported, limited functionality
4. **Performance**: Slower than real AWS services
5. **Features**: Some advanced AWS features not available

### Workarounds

For services not supported by LocalStack:

1. **Bedrock**: Use mock responses or actual AWS (with test account)
2. **Transcribe**: Use HTTP transcription service (Vosk) instead
3. **Textract**: Use iText7 for PDF processing locally

## CI/CD with LocalStack

### GitHub Actions Example

```yaml
name: Test with LocalStack

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    
    services:
      localstack:
        image: localstack/localstack:latest
        env:
          SERVICES: s3,secretsmanager,lambda
        ports:
          - 4566:4566
      
      postgres:
        image: pgvector/pgvector:pg17
        env:
          POSTGRES_PASSWORD: postgres
        ports:
          - 5432:5432
      
      redis:
        image: redis:latest
        ports:
          - 6379:6379
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      
      - name: Initialize LocalStack
        run: |
          aws --endpoint-url=http://localhost:4566 s3 mb s3://smart-glasses-dev-media
          aws --endpoint-url=http://localhost:4566 s3 mb s3://smart-glasses-dev-documents
      
      - name: Run tests
        env:
          AWS_ACCESS_KEY_ID: test
          AWS_SECRET_ACCESS_KEY: test
          AWS_ENDPOINT_URL: http://localhost:4566
        run: dotnet test
```

## Best Practices

1. **Cleanup After Tests**: Always clean up resources after tests
2. **Isolated Tests**: Each test should create its own resources
3. **Use Test Prefixes**: Prefix test resources with `test-` or `temp-`
4. **Mock External Services**: Mock Bedrock and Transcribe when not available
5. **Fast Tests**: Keep tests fast by minimizing I/O operations
6. **Parallel Testing**: Design tests to run in parallel safely

## Additional Resources

- [LocalStack Documentation](https://docs.localstack.cloud/)
- [AWS CLI Documentation](https://docs.aws.amazon.com/cli/)
- [.NET AWS SDK Documentation](https://docs.aws.amazon.com/sdk-for-net/)
- [Playwright Documentation](https://playwright.dev/)

## Support

For issues with LocalStack testing:
1. Check LocalStack logs: `docker-compose logs localstack`
2. Verify AWS CLI configuration
3. Check network connectivity
4. Review test configuration
5. Consult LocalStack GitHub issues: https://github.com/localstack/localstack/issues
