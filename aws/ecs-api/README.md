# ECS API Service

This directory contains the Dockerfile and configuration for deploying the Smart Glasses Middleware API to AWS ECS Fargate.

## Changes from Original

The API has been updated to use AWS-native services:

1. **Storage**: MinIO → Amazon S3
2. **AI/LLM**: Ollama → Amazon Bedrock
3. **Database**: PostgreSQL (Docker) → Amazon RDS Aurora PostgreSQL with pgvector
4. **Cache**: Redis (Docker) → Amazon ElastiCache Redis
5. **Transcription**: Vosk (Docker) → Amazon Transcribe (or self-hosted on ECS)
6. **Secrets**: .env file → AWS Secrets Manager

## Building the Docker Image

```bash
# From the repository root
docker build -t smart-glasses-api:latest -f aws/ecs-api/Dockerfile .
```

## Pushing to ECR

```bash
# Login to ECR
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin <account-id>.dkr.ecr.us-east-1.amazonaws.com

# Tag image
docker tag smart-glasses-api:latest <account-id>.dkr.ecr.us-east-1.amazonaws.com/smart-glasses-dev-api:latest

# Push to ECR
docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/smart-glasses-dev-api:latest
```

## Environment Variables

The container expects configuration from:

1. **Secrets Manager**: Database credentials, Redis auth token
2. **Environment Variables**: AWS region, bucket names, Bedrock model IDs

See `aws/infrastructure/ecs.tf` for the full list of environment variables and secrets.

## Health Check

The container exposes a `/health` endpoint on port 5000 for ECS health checks.

## Logs

Logs are sent to CloudWatch Logs at:
- Log Group: `/ecs/smart-glasses-{environment}/api`
- Stream: `ecs/{task-id}`

## Deployment

Deployment is handled by:
1. GitHub Actions CI/CD pipeline (see `.github/workflows/deploy-aws.yml`)
2. Manual deployment via AWS CLI/Console
3. Terraform apply (updates ECS task definition)
