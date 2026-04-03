# AWS LocalStack Deployment

This directory contains AWS-specific code and infrastructure configurations for deploying the Smart Glasses Middleware using LocalStack for local development.

## What's Included

- `LOCALSTACK.md` - Complete LocalStack setup and testing guide
- `docker-compose.localstack.yml` - LocalStack docker compose configuration (in root directory)
- `infrastructure/` - Terraform configurations for AWS resources
- `lambda-document-processor/` - Lambda function for document processing
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

When deploying through GitHub Actions (`deploy-main.yml` or `deploy-fast.yml`),
the `verify-deployment` job prints the website URL in logs as:

- `Deployment URL: https://...`

Look for the `Print deployed website URL` step in that job.

### Cost-Optimized Non-Prod Defaults

The Terraform in `aws/infrastructure` now defaults to lower idle cost for non-prod:
- ECS API desired/min tasks default to `0`
- Redis/ElastiCache is disabled by default in non-prod
- ECS Container Insights is disabled by default in non-prod
- Interface VPC endpoints default to disabled in non-prod when cost optimization is enabled
- S3 bucket versioning defaults to suspended in non-prod when cost optimization is enabled
- RDS Performance Insights and Enhanced Monitoring default to disabled
- NAT gateways default to disabled

Optional ultra-cheap mode for dev:
- `dev_public_network_mode=true` runs ECS API tasks in public subnets with public IPs (so ECR/Logs/Secrets access can use internet egress instead of paid interface endpoints).
- Tradeoff: reduced network isolation compared with private-subnet + PrivateLink architecture.

If you want always-on behavior, override these at apply time:

```bash
terraform apply \
	-var="environment=dev" \
	-var="cost_optimized_non_prod=false" \
	-var="dev_public_network_mode=false" \
	-var="enable_s3_versioning=true" \
	-var="ecs_api_desired_count=1" \
	-var="ecs_api_min_capacity=1" \
	-var="enable_redis=true" \
	-var="enable_nat_gateway=true"
```

### Optional Wake On Access + Idle Sleep

Terraform now includes an ECS idle autoscaler for the API service:
- When ALB request traffic appears, CloudWatch alarm triggers a Lambda that sets ECS desired count to `ecs_warm_desired_count` (default `1`)
- After `ecs_idle_timeout_minutes` of no traffic (default `15`), another alarm triggers Lambda to set desired count to `0`
- The same wake path is exposed through CloudFront `/wake`; when invoked from the website, it can also request RDS start if the dev DB is currently stopped

This gives near-zero compute cost while idle, with a brief cold-start delay on first request after sleep.

Variables:
- `enable_ecs_idle_autoscaler` (default: `false`)
- `ecs_warm_desired_count` (default: `1`)
- `ecs_idle_timeout_minutes` (default: `15`)

### Dev Park / Wake (Lowest Cost Mode)

For the lowest dev cost, use the GitHub Actions workflow `.github/workflows/dev-park.yml`:

- `park`: sets ECS autoscaling max/min to `0`, sets ECS desired count to `0`, and requests `stop-db-instance` for `smart-glasses-dev-postgres`.
- `wake`: starts the dev DB (if stopped), restores ECS autoscaling envelope (`min=0`, `max=1`), and sets ECS desired count to `1`.
- `status`: prints current ECS and RDS state only.

The same workflow also runs a nightly scheduled `park` action automatically.

How to run manually:

1. GitHub → Actions → **Dev Park / Wake**
2. Click **Run workflow**
3. Choose action: `park`, `wake`, or `status`

Notes:
- While parked, API-dependent features are offline by design; static frontend can still load.
- If RDS has been stopped for multiple days, wake-up can take longer on first access.

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
