# AWS Deployment Guide

This guide explains how to deploy the Smart Glasses Middleware to AWS using the provided infrastructure-as-code and CI/CD pipeline.

## Architecture Overview

The application is deployed on AWS using the following services:

### Core Services
- **Amazon ECS Fargate**: Container orchestration for the API service
- **Amazon RDS Aurora PostgreSQL**: Managed database with pgvector extension
- **Amazon ElastiCache Redis**: Managed caching and pub/sub
- **Amazon S3**: Object storage for media and documents
- **AWS Lambda**: Serverless document processing
- **Amazon Bedrock**: LLM and embedding generation
- **Amazon Transcribe**: Speech-to-text (or self-hosted Vosk on ECS)

### Supporting Services
- **Application Load Balancer**: Load balancing and SSL termination
- **Amazon CloudFront**: CDN for frontend and API
- **AWS Secrets Manager**: Secure credential storage
- **Amazon ECR**: Docker image registry
- **AWS VPC**: Network isolation and security
- **CloudWatch**: Logging and monitoring

## Prerequisites

1. **AWS Account**: With appropriate permissions
2. **AWS CLI**: Version 2.x installed and configured
3. **Terraform**: Version 1.0 or later
4. **Docker**: For building container images
5. **.NET SDK**: Version 10.0 for local development
6. **Python**: Version 3.12 for Lambda function

## Initial Setup

### 1. Configure AWS Credentials

```bash
aws configure
# Enter your AWS Access Key ID
# Enter your AWS Secret Access Key
# Enter your default region (e.g., us-east-1)
```

### 2. Create S3 Bucket for Terraform State

```bash
aws s3 mb s3://your-terraform-state-bucket --region us-east-1
aws s3api put-bucket-versioning --bucket your-terraform-state-bucket --versioning-configuration Status=Enabled
```

### 3. Configure Terraform Backend

Edit `aws/infrastructure/main.tf` to configure the S3 backend:

```hcl
backend "s3" {
  bucket = "your-terraform-state-bucket"
  key    = "smart-glasses/terraform.tfstate"
  region = "us-east-1"
}
```

### 4. Initialize Terraform

```bash
cd aws/infrastructure
terraform init
```

## Deployment Steps

### 1. Deploy Infrastructure

```bash
cd aws/infrastructure

# Create a terraform.tfvars file
cat > terraform.tfvars <<EOF
aws_region = "us-east-1"
environment = "dev"
project_name = "smart-glasses"
domain_name = "api.yourdomain.com" # Optional
certificate_arn = "arn:aws:acm:..." # Optional
EOF

# Plan the deployment
terraform plan

# Apply the infrastructure
terraform apply
```

This will create:
- VPC with public and private subnets
- RDS Aurora PostgreSQL cluster with pgvector
- ElastiCache Redis cluster
- S3 buckets for media, documents, and frontend
- ECS cluster and task definitions
- Application Load Balancer
- CloudFront distribution
- Lambda function for document processing
- All necessary IAM roles and security groups

### 2. Enable pgvector Extension

After RDS is created, connect to the database and enable pgvector:

```bash
# Get database endpoint from Terraform output
terraform output rds_endpoint

# Connect using psql
psql -h <rds-endpoint> -U postgres -d smartglasses

# Enable pgvector extension
CREATE EXTENSION IF NOT EXISTS vector;
```

### 3. Build and Push Docker Image

```bash
# From repository root

# Get ECR login
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin <account-id>.dkr.ecr.us-east-1.amazonaws.com

# Build image
docker build -t smart-glasses-dev-api:latest -f aws/ecs-api/Dockerfile .

# Tag and push
docker tag smart-glasses-dev-api:latest <account-id>.dkr.ecr.us-east-1.amazonaws.com/smart-glasses-dev-api:latest
docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/smart-glasses-dev-api:latest
```

### 4. Deploy Lambda Function

```bash
cd aws/lambda-document-processor

# Build deployment package
chmod +x build.sh
./build.sh

# Deploy to AWS
aws lambda update-function-code \
  --function-name smart-glasses-dev-document-processor \
  --zip-file fileb://deployment.zip
```

### 5. Run Database Migrations

```bash
# Get database connection string from Secrets Manager
aws secretsmanager get-secret-value --secret-id smart-glasses-dev-db-credentials --query SecretString --output text

# Run EF Core migrations
cd src/SmartGlassesMiddleware
dotnet ef database update --connection "<connection-string>"
```

### 6. Deploy Frontend (if applicable)

```bash
# Build frontend
cd frontend
npm run build

# Upload to S3
aws s3 sync build/ s3://smart-glasses-dev-frontend/

# Invalidate CloudFront cache
aws cloudfront create-invalidation --distribution-id <distribution-id> --paths "/*"
```

## Automated Deployment with GitHub Actions

### 1. Configure GitHub Secrets

Add the following secrets to your GitHub repository:

- `AWS_ACCESS_KEY_ID`: AWS access key
- `AWS_SECRET_ACCESS_KEY`: AWS secret key
- `API_URL`: API endpoint URL (for E2E tests)

### 2. Trigger Deployment

Deployments are automatically triggered on push to:
- `main` branch → Production
- `develop` branch → Development

Or manually via workflow dispatch in GitHub Actions.

## Testing with LocalStack

LocalStack allows you to test AWS services locally.

### 1. Start LocalStack

```bash
docker-compose -f docker-compose.localstack.yml up -d
```

### 2. Configure AWS CLI for LocalStack

```bash
export AWS_ENDPOINT_URL=http://localhost:4566
export AWS_ACCESS_KEY_ID=test
export AWS_SECRET_ACCESS_KEY=test
export AWS_DEFAULT_REGION=us-east-1
```

### 3. Run Tests

```bash
# Unit tests with LocalStack
dotnet test

# E2E tests
npm test
```

## Monitoring and Maintenance

### CloudWatch Logs

```bash
# View ECS logs
aws logs tail /ecs/smart-glasses-dev/api --follow

# View Lambda logs
aws logs tail /aws/lambda/smart-glasses-dev-document-processor --follow
```

### Scaling

ECS tasks auto-scale based on CPU and memory utilization. To adjust scaling:

```bash
# Update desired count
aws ecs update-service \
  --cluster smart-glasses-dev-cluster \
  --service smart-glasses-dev-api-service \
  --desired-count 3
```

### Database Backups

Aurora automatically backs up your database. To create a manual snapshot:

```bash
aws rds create-db-cluster-snapshot \
  --db-cluster-snapshot-identifier smart-glasses-manual-snapshot \
  --db-cluster-identifier smart-glasses-dev-aurora-cluster
```

## Cost Optimization

1. **Use Aurora Serverless v2**: Auto-scales based on load
2. **Enable S3 lifecycle policies**: Archive old files to Glacier
3. **Use Fargate Spot**: For non-production workloads
4. **Configure CloudWatch log retention**: Limit retention to 7-30 days
5. **Use VPC endpoints**: Reduce NAT Gateway costs

## Troubleshooting

### ECS Task Fails to Start

Check:
- CloudWatch logs for error messages
- Task definition has correct environment variables
- Security groups allow traffic
- IAM role has necessary permissions

### Database Connection Issues

Check:
- Security group allows traffic from ECS tasks
- Secrets Manager has correct credentials
- VPC configuration is correct

### Lambda Function Errors

Check:
- CloudWatch logs for detailed error messages
- Lambda has VPC access (if needed)
- IAM role has necessary permissions
- Environment variables are set correctly

## Cleanup

To destroy all AWS resources:

```bash
cd aws/infrastructure
terraform destroy
```

**Warning**: This will delete all data. Make sure to backup any important data before running this command.

## Security Best Practices

1. **Enable MFA** on AWS account
2. **Use IAM roles** instead of access keys where possible
3. **Rotate credentials** regularly
4. **Enable CloudTrail** for audit logging
5. **Use AWS Config** for compliance monitoring
6. **Enable GuardDuty** for threat detection
7. **Implement least privilege** IAM policies
8. **Use Secrets Manager** for all credentials
9. **Enable encryption** at rest and in transit
10. **Regular security audits** and patching

## Support

For issues or questions:
- Check the [README](../README.md) for general information
- Review CloudWatch logs for error details
- Check AWS service health dashboard
- Consult AWS documentation for service-specific issues
