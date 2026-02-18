# AWS Deployment - Quick Reference

This is a quick reference guide for AWS deployment. For detailed instructions, see the files in the `aws/` directory.

## Architecture

The application can be deployed in two modes:

### 1. Docker Compose (Development/Self-Hosted)
- MinIO for object storage
- Ollama for AI/LLM
- PostgreSQL for database
- Redis for caching
- Vosk for transcription

### 2. AWS (Production/Cloud)
- Amazon S3 for object storage
- Amazon Bedrock for AI/LLM
- Amazon RDS Aurora for database
- Amazon ElastiCache for caching
- Whisper Lambda (self-hosted) for transcription
- AWS Lambda for document processing
- Amazon ECS Fargate for API hosting
- Amazon CloudFront for CDN

## Key Documentation

| Document | Purpose |
|----------|---------|
| [aws/README.md](aws/README.md) | Comprehensive AWS architecture overview |
| [aws/DEPLOYMENT.md](aws/DEPLOYMENT.md) | Step-by-step deployment guide |
| [aws/MIGRATION.md](aws/MIGRATION.md) | Migration guide from Docker to AWS |
| [aws/LOCALSTACK.md](aws/LOCALSTACK.md) | Local testing with LocalStack |

## Quick Start - AWS Deployment

### Prerequisites
- AWS Account
- AWS CLI configured
- Terraform >= 1.0
- Docker

### Deploy Infrastructure

```bash
cd aws/infrastructure
terraform init
terraform apply
```

### Build and Deploy API

```bash
# Build Docker image
docker build -t smart-glasses-api -f aws/ecs-api/Dockerfile .

# Login to ECR
aws ecr get-login-password --region us-east-1 | \
  docker login --username AWS --password-stdin <account-id>.dkr.ecr.us-east-1.amazonaws.com

# Push to ECR
docker tag smart-glasses-api:latest <ecr-url>:latest
docker push <ecr-url>:latest
```

### Deploy Lambda

```bash
cd aws/lambda-document-processor
./build.sh
aws lambda update-function-code \
  --function-name smart-glasses-dev-document-processor \
  --zip-file fileb://deployment.zip
```

## Quick Start - LocalStack Testing

### Start LocalStack

```bash
docker-compose -f docker-compose.localstack.yml up -d
```

### Run Tests

```bash
# Unit tests
dotnet test

# E2E tests
npm test
```

## Configuration

### AWS Configuration (appsettings.AWS.json or environment variables)

```json
{
  "AWS": {
    "Region": "us-east-1",
    "UseLocalStack": false,
    "S3": {
      "MediaBucket": "your-media-bucket",
      "DocumentsBucket": "your-documents-bucket"
    },
    "Bedrock": {
      "ModelId": "anthropic.claude-3-sonnet-20240229-v1:0",
      "EmbeddingModelId": "amazon.titan-embed-text-v2:0"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=rds-endpoint;Database=smartglasses;...",
    "Redis": "elasticache-endpoint:6379"
  }
}
```

### LocalStack Configuration

```json
{
  "AWS": {
    "UseLocalStack": true,
    "Endpoint": "http://localhost:4566",
    "Region": "us-east-1"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=smartglasses;...",
    "Redis": "localhost:6379"
  }
}
```

## Service Implementations

| Service | Legacy Implementation | AWS Implementation |
|---------|----------------------|-------------------|
| Storage | MinioMediaStorageService | S3MediaStorageService |
| AI/LLM | OllamaAIService | BedrockAIService |
| Transcription | HttpTranscriptionService (Vosk) | AwsTranscribeService or Whisper Lambda |
| Database | PostgreSQL (Docker) | RDS Aurora PostgreSQL |
| Cache | Redis (Docker) | ElastiCache Redis |
| Document Processing | Background Service | Lambda Function |

## GitHub Actions

The repository includes a GitHub Actions workflow (`.github/workflows/deploy-aws.yml`) that automatically:

1. Runs tests on push/PR
2. Builds Docker images
3. Pushes to ECR
4. Deploys to ECS
5. Updates Lambda functions
6. Runs E2E tests

### Required Secrets

Add these secrets to your GitHub repository:

- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `API_URL` (for E2E tests)

## Cost Estimates

### Development Environment
- **Monthly**: ~$150-300
- **Hourly**: ~$0.20-0.40

### Production Environment (varies by traffic)
- **Monthly**: $500-2000+
- Use AWS Cost Calculator for accurate estimates

## Monitoring

### CloudWatch Logs
```bash
# View API logs
aws logs tail /ecs/smart-glasses-dev/api --follow

# View Lambda logs
aws logs tail /aws/lambda/smart-glasses-dev-document-processor --follow
```

### Metrics
- ECS CPU/Memory utilization
- RDS connections/queries
- ElastiCache hit rate
- Lambda invocations/errors
- S3 request counts

## Troubleshooting

### Common Issues

1. **Database connection failed**
   - Check security groups
   - Verify connection string
   - Check VPC configuration

2. **S3 access denied**
   - Verify IAM roles
   - Check bucket policies
   - Verify bucket names

3. **Lambda timeout**
   - Increase timeout setting
   - Check VPC configuration
   - Review CloudWatch logs

4. **High costs**
   - Review CloudWatch metrics
   - Check S3 lifecycle policies
   - Optimize Aurora scaling
   - Use Fargate Spot for non-prod

## Support

- **AWS Issues**: Check CloudWatch logs and AWS service health
- **Application Issues**: Check application logs in CloudWatch
- **Infrastructure Issues**: Review Terraform state and AWS Console
- **LocalStack Issues**: Check LocalStack logs and GitHub issues

## Next Steps

1. Read [aws/README.md](aws/README.md) for architecture details
2. Follow [aws/DEPLOYMENT.md](aws/DEPLOYMENT.md) for deployment
3. Use [aws/LOCALSTACK.md](aws/LOCALSTACK.md) for local testing
4. Review [aws/MIGRATION.md](aws/MIGRATION.md) for migration from Docker
5. To switch transcription to a Whisper Lambda, see `aws/lambda-whisper` and `aws/infrastructure/whisper_lambda.tf` for templates.

## Whisper Lambda - Quick Instructions

1. Create an ECR repository for the whisper lambda (example name `smart-glasses-whisper`).

2. Build and push the container image (from `aws/lambda-whisper`):

```powershell
# Build, tag, and push (replace <ECR_URI> with your repo URI)
cd aws\lambda-whisper
./build.sh <ECR_URI> latest
```

3. Deploy the Lambda via Terraform by passing the image URI to the module/terraform file:

```powershell
cd aws\infrastructure
terraform init
terraform apply -var "whisper_image_uri=<ECR_URI>:latest"
```

4. Update application AWS config (e.g., `appsettings.AWS.json`) to enable the Whisper Lambda:

```json
"AWS": {
  "Transcription": {
    "UseWhisperLambda": true,
    "WhisperLambdaFunction": "smart-glasses-whisper",
    "TranscriptionTimeoutSeconds": 300
  }
}
```

5. Redeploy the API (ECS) so it picks up the new configuration and DI registration.

Notes:
- The sample `aws/lambda-whisper` image uses `whisper`/`torch` and will be large; choose model `tiny` or `base` for lower memory and faster startup.
- Ensure the Lambda has sufficient memory (>=2048MB) and timeout (>=120s) for longer audio.
- Add IAM permissions if the Lambda needs to access S3 (for fetching audio) instead of sending bytes in the payload.

## Additional Resources

- [AWS Documentation](https://docs.aws.amazon.com/)
- [Terraform AWS Provider](https://registry.terraform.io/providers/hashicorp/aws/latest/docs)
- [LocalStack Documentation](https://docs.localstack.cloud/)
- [AWS Well-Architected Framework](https://aws.amazon.com/architecture/well-architected/)
