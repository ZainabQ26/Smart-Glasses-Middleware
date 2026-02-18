# AWS LocalStack Deployment

This directory contains AWS-specific code and infrastructure configurations for deploying the Smart Glasses Middleware using LocalStack for local development.

## What's Included

- `LOCALSTACK.md` - Complete LocalStack setup and testing guide
- `docker-compose.localstack.yml` - LocalStack docker compose configuration (in root directory)
- `infrastructure/` - Terraform configurations for AWS resources
- `lambda-document-processor/` - Lambda function for document processing
- `lambda-whisper/` - Lambda function for audio transcription
- `ecs-api/` - ECS container configurations
- `sam/` - AWS SAM (Serverless Application Model) templates

## Quick Start

1. **Start LocalStack:**
```bash
cd ..
docker-compose -f docker-compose.localstack.yml up -d
```

2. **Configure AWS CLI:**
```bash
aws configure set aws_access_key_id test --profile localstack
aws configure set aws_secret_access_key test --profile localstack
aws configure set region us-east-1 --profile localstack
```

3. **Verify LocalStack:**
```bash
aws --endpoint-url=http://localhost:4566 s3 ls
```

## Documentation

See [LOCALSTACK.md](LOCALSTACK.md) for complete documentation on:
- LocalStack setup and configuration
- VS Code AWS Toolkit integration
- Testing AWS services locally
- Deploying Lambda functions
- Working with S3, Secrets Manager, and other AWS services

## Production Deployment

For production deployment to real AWS (not LocalStack):
1. Configure Terraform backend with real AWS credentials
2. Update `infrastructure/` Terraform files with production values
3. Run `terraform apply` in the `infrastructure/` directory
4. Deploy containers to ECS and Lambda functions

## Architecture

### LocalStack Services
- **S3**: Object storage (media, documents, frontend)
- **Secrets Manager**: Credential management
- **RDS/PostgreSQL**: Database with pgvector (runs in separate container)
- **ElastiCache/Redis**: Cache and pub/sub (runs in separate container)
- **Lambda**: Serverless functions (document processing, transcription)
- **ECS**: Container orchestration (API service)

### Production AWS Services
When deploying to production AWS, the same services are used but with managed AWS services instead of LocalStack.
