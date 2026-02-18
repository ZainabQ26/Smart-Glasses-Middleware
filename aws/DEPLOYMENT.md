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

### Local helpers (run without installing Terraform locally)

Two helper scripts are provided in the repository under `scripts/`:

- `scripts/terraform_docker.ps1` — runs `terraform` inside the official HashiCorp Docker image. Example usages:

```powershell
# Validate terraform
.\scripts\terraform_docker.ps1 -Cmd validate

# Init (no remote backend) and plan
.\scripts\terraform_docker.ps1 -Cmd "init -input=false"
.\scripts\terraform_docker.ps1 -Cmd plan

# Apply (requires AWS creds in env)
.\scripts\terraform_docker.ps1 -Cmd apply
```

- `scripts/start_localstack.ps1` — start LocalStack via `docker-compose.localstack.yml` for local testing of S3, SQS, Lambda, etc. Example:

```powershell
.\scripts\start_localstack.ps1
```

Notes:
- The `terraform_docker.ps1` helper mounts `aws/infrastructure` into the container. Provide AWS credentials in your shell (or use a local profile) so Terraform can authenticate when applying.
- These helpers let you run `terraform validate` and basic plans without installing the Terraform binary locally.

## Using the AWS Toolkit in VS Code

The AWS Toolkit extension for VS Code lets you view AWS resources, deploy functions, and invoke Lambdas directly from the editor. Suggested flow:

1. Install the **AWS Toolkit** extension and sign in to an AWS profile (or select the credentials the Toolkit should use).

2. Use the VS Code **Run Task** UI to run the helper tasks added to the repository (`.vscode/tasks.json`):

  - `Start LocalStack` — start LocalStack for local testing.
  - `Terraform: init/validate/plan/apply (LocalStack)` — run Terraform inside Docker for local infra tests.
  - `Build: Whisper Lambda image` and `Build: API image` — build images locally.
  - `Push: ... to ECR` — push images to your ECR (requires AWS credentials in the environment).

3. Deploy a Lambda from an image with the Toolkit:

  - In the AWS Explorer, expand **ECR** to confirm the image exists (or use the pushed image URI).
  - Right-click **Lambda** and choose **Deploy Lambda from container image** (Toolkit will prompt for function name, memory, timeout, and environment variables).

4. Invoke and test the Lambda from the Toolkit:

  - Right-click the function → **Invoke on AWS** to run test events and view logs.
  - For the API, run the `Run API locally` task to exercise the sync fast-path locally, or deploy the API via Terraform/CI and test the provisioned endpoint.

Notes:

- The Toolkit uses your local AWS credentials/profile; ensure the profile has permissions to push to ECR and deploy Lambdas.
- LocalStack provides good coverage for S3, SQS, and Lambda, but complex services like RDS/ElastiCache are partially supported — prefer a dev AWS account for full integration tests.
- The `.vscode/tasks.json` in the repo provides one-click tasks to build, push, run Terraform (via Docker), and start LocalStack.


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

## Deployment Checklist (GitHub Actions)

Before pushing to `main`, ensure the following repository secrets are set in GitHub (Settings → Secrets):

- `AWS_ACCESS_KEY_ID` — IAM user or deploy role access key with permissions to create ECR, ECS, Lambda, S3, SQS, RDS, CloudFront, IAM.
- `AWS_SECRET_ACCESS_KEY` — Secret key for the above.
- `AWS_REGION` — e.g. `us-east-1`.
- `AWS_ACCOUNT_ID` — Your AWS account ID (used for forming ECR URIs).
- `DB_NAME` — Database name used by EF migrations (e.g. `smartglasses`).
- `DB_USER` — DB admin/user for EF migrations.
- `DB_PASSWORD` — DB password used for migrations (stored as a secret only used by workflow).

Also verify these repository prerequisites:

- The Terraform module `aws/infrastructure` exposes an output named `rds_endpoint` (workflow expects this). If different, update the workflow.
- The Terraform config accepts optional variables `api_image`, `lambda_image`, and `client_image` (CI will write `tf_image_vars.auto.tfvars`).
- Ensure an S3 backend is configured and accessible for Terraform state before running `terraform init`.

Quick workflow verification steps (run locally):

```bash
# Validate Terraform in the infra folder
cd aws/infrastructure
terraform fmt -check
terraform validate

# Lint Dockerfiles (optional)
hadolint aws/lambda-whisper/Dockerfile
hadolint src/SmartGlassesMiddleware/Dockerfile
```

After these are set, pushing to `main` will trigger the `.github/workflows/deploy-main.yml` workflow which:

- Builds & pushes images to ECR using the commit SHA as tag
- Writes `api_image`, `lambda_image`, `client_image` into a `tf_image_vars.auto.tfvars` used by Terraform
- Runs `terraform apply` in `aws/infrastructure`
- Runs EF Core migrations against the provisioned RDS endpoint
