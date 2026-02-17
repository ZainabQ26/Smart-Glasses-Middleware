# Migration Guide: Docker Compose to AWS

This guide provides step-by-step instructions for migrating the Smart Glasses Middleware from Docker Compose to AWS.

## Overview

This migration transforms the application from a self-hosted containerized deployment to a cloud-native AWS architecture.

## Pre-Migration Checklist

- [ ] AWS account with appropriate permissions
- [ ] AWS CLI installed and configured
- [ ] Terraform installed (>= 1.0)
- [ ] Docker installed
- [ ] .NET 10 SDK installed
- [ ] Python 3.12 installed (for Lambda)
- [ ] Backup of existing data
- [ ] Domain name (optional, but recommended)
- [ ] SSL certificate in ACM (optional)

## Migration Steps

### Step 1: Data Export

Before migrating, export existing data from the Docker Compose environment.

#### 1.1 Export PostgreSQL Data

```bash
# Connect to postgres container
docker exec -it postgres bash

# Export database
pg_dump -U postgres smartglasses > /tmp/smartglasses_backup.sql

# Copy to host
docker cp postgres:/tmp/smartglasses_backup.sql ./smartglasses_backup.sql
```

#### 1.2 Export MinIO Data

```bash
# Install MinIO client
wget https://dl.min.io/client/mc/release/linux-amd64/mc
chmod +x mc

# Configure MinIO client
./mc alias set local http://localhost:9000 minioadmin minioadmin123

# Export buckets
./mc mirror local/smart-glasses-media ./minio-backup/media
./mc mirror local/smart-glasses-documents ./minio-backup/documents
```

#### 1.3 Export Redis Data (if needed)

```bash
# Redis data is typically cache - may not need to export
# If needed:
docker exec -it redis redis-cli SAVE
docker cp redis:/data/dump.rdb ./redis-backup/
```

### Step 2: Deploy AWS Infrastructure

#### 2.1 Configure Terraform

```bash
cd aws/infrastructure

# Create terraform.tfvars
cat > terraform.tfvars <<EOF
aws_region              = "us-east-1"
environment             = "dev"
project_name            = "smart-glasses"
vpc_cidr                = "10.0.0.0/16"
availability_zones      = ["us-east-1a", "us-east-1b"]
db_instance_class       = "db.serverless"
elasticache_node_type   = "cache.t3.micro"
ecs_api_cpu             = 1024
ecs_api_memory          = 2048
ecs_api_desired_count   = 2
enable_nat_gateway      = true
domain_name             = "api.yourdomain.com"  # Optional
certificate_arn         = ""                      # Optional
EOF
```

#### 2.2 Initialize and Deploy

```bash
# Initialize Terraform
terraform init

# Review plan
terraform plan

# Apply (this will take 25-40 minutes - RDS Aurora alone takes 10-15 minutes)
terraform apply

# Save outputs
terraform output > ../terraform-outputs.txt
```

### Step 3: Configure Database

#### 3.1 Enable pgvector Extension

```bash
# Get RDS endpoint from Terraform output
RDS_ENDPOINT=$(terraform output -raw rds_endpoint)

# Get credentials from Secrets Manager
aws secretsmanager get-secret-value \
  --secret-id smart-glasses-dev-db-credentials \
  --query SecretString \
  --output text | jq -r '.password' > /tmp/db_pass.txt

# Connect to database
PGPASSWORD=$(cat /tmp/db_pass.txt) psql \
  -h $RDS_ENDPOINT \
  -U postgres \
  -d smartglasses \
  -c "CREATE EXTENSION IF NOT EXISTS vector;"

# Clean up password file
rm /tmp/db_pass.txt
```

#### 3.2 Import Data

```bash
# Import database dump
PGPASSWORD=$(aws secretsmanager get-secret-value \
  --secret-id smart-glasses-dev-db-credentials \
  --query SecretString --output text | jq -r '.password') \
psql -h $RDS_ENDPOINT -U postgres -d smartglasses < smartglasses_backup.sql
```

### Step 4: Upload Files to S3

#### 4.1 Configure AWS CLI

```bash
# Get bucket names from Terraform output
MEDIA_BUCKET=$(terraform output -raw s3_media_bucket)
DOCS_BUCKET=$(terraform output -raw s3_documents_bucket)
```

#### 4.2 Upload Media Files

```bash
# Upload media files
aws s3 sync ./minio-backup/media/ s3://$MEDIA_BUCKET/media/

# Upload document files
aws s3 sync ./minio-backup/documents/ s3://$DOCS_BUCKET/documents/
```

### Step 5: Build and Deploy API

#### 5.1 Build Docker Image

```bash
# From repository root
cd ../..

# Get ECR repository URL
ECR_REPO=$(cd aws/infrastructure && terraform output -raw ecr_repository_url)

# Login to ECR
aws ecr get-login-password --region us-east-1 | \
  docker login --username AWS --password-stdin $ECR_REPO

# Build image
docker build -t smart-glasses-api:latest -f aws/ecs-api/Dockerfile .

# Tag and push
docker tag smart-glasses-api:latest $ECR_REPO:latest
docker push $ECR_REPO:latest
```

#### 5.2 Run Database Migrations

```bash
# Get connection string
CONNECTION_STRING=$(aws secretsmanager get-secret-value \
  --secret-id smart-glasses-dev-db-credentials \
  --query SecretString --output text | jq -r '.connection_string')

# Run migrations
cd src/SmartGlassesMiddleware
dotnet ef database update --connection "$CONNECTION_STRING"
```

#### 5.3 Update ECS Service

```bash
# ECS service is automatically updated by Terraform
# Force new deployment to use latest image
cd ../../aws/infrastructure
ECS_CLUSTER=$(terraform output -raw ecs_cluster_name)
ECS_SERVICE=$(terraform output -json | jq -r '.ecs_service_name.value')

aws ecs update-service \
  --cluster $ECS_CLUSTER \
  --service $ECS_SERVICE \
  --force-new-deployment
```

### Step 6: Deploy Lambda Function

#### 6.1 Build Lambda Package

```bash
cd ../lambda-document-processor
chmod +x build.sh
./build.sh
```

#### 6.2 Deploy to AWS

```bash
LAMBDA_ARN=$(cd ../infrastructure && terraform output -raw lambda_document_processor_arn)

aws lambda update-function-code \
  --function-name smart-glasses-dev-document-processor \
  --zip-file fileb://deployment.zip
```

### Step 7: Verify Deployment

#### 7.1 Check Service Health

```bash
# Get ALB DNS name
ALB_DNS=$(cd ../infrastructure && terraform output -raw api_alb_dns)

# Test health endpoint
curl https://$ALB_DNS/health

# Expected response: {"status":"Healthy"}
```

#### 7.2 Check CloudWatch Logs

```bash
# View ECS logs
aws logs tail /ecs/smart-glasses-dev/api --follow

# View Lambda logs
aws logs tail /aws/lambda/smart-glasses-dev-document-processor --follow
```

#### 7.3 Test API Endpoints

```bash
# Test text endpoint
curl -X POST https://$ALB_DNS/api/text \
  -H "Content-Type: application/json" \
  -d '{"text": "What time is it?"}'

# Test document upload
curl -X POST https://$ALB_DNS/api/documents/upload \
  -H "Content-Type: multipart/form-data" \
  -F "file=@sample.pdf"
```

### Step 8: Configure DNS and CloudFront

#### 8.1 Update DNS Records

```bash
# Get CloudFront distribution domain
CF_DOMAIN=$(cd aws/infrastructure && terraform output -raw cloudfront_distribution_domain)

# Create CNAME record in your DNS provider
# yourdomain.com -> $CF_DOMAIN
```

#### 8.2 Update CORS Settings

Update the CloudFront distribution to allow your domain:

```bash
# Update terraform.tfvars with your domain
# Then re-apply
cd aws/infrastructure
terraform apply
```

### Step 9: Run Tests

#### 9.1 Unit Tests

```bash
cd ../../
dotnet test
```

#### 9.2 E2E Tests

```bash
# Update API URL in .env for tests
export API_URL=https://$ALB_DNS

npm test
```

### Step 10: Monitor and Optimize

#### 10.1 Set Up CloudWatch Alarms

```bash
# ECS CPU alarm
aws cloudwatch put-metric-alarm \
  --alarm-name smart-glasses-dev-ecs-cpu \
  --alarm-description "ECS CPU utilization high" \
  --metric-name CPUUtilization \
  --namespace AWS/ECS \
  --statistic Average \
  --period 300 \
  --threshold 80 \
  --comparison-operator GreaterThanThreshold \
  --evaluation-periods 2

# Lambda errors alarm
aws cloudwatch put-metric-alarm \
  --alarm-name smart-glasses-dev-lambda-errors \
  --alarm-description "Lambda function errors" \
  --metric-name Errors \
  --namespace AWS/Lambda \
  --statistic Sum \
  --period 60 \
  --threshold 5 \
  --comparison-operator GreaterThanThreshold \
  --evaluation-periods 1
```

#### 10.2 Enable Enhanced Monitoring

```bash
# Enable Container Insights for ECS
aws ecs update-cluster-settings \
  --cluster smart-glasses-dev-cluster \
  --settings name=containerInsights,value=enabled
```

## Post-Migration Tasks

### 1. Decommission Old Infrastructure

After verifying everything works:

```bash
# Stop Docker Compose services
docker-compose down

# Remove volumes (only after confirming migration)
docker-compose down -v

# Optional: Remove Docker images
docker image prune -a
```

### 2. Update Documentation

- [ ] Update README.md with new deployment instructions
- [ ] Update environment variable documentation
- [ ] Update CI/CD documentation
- [ ] Update team runbooks

### 3. Set Up Monitoring

- [ ] Configure CloudWatch dashboards
- [ ] Set up log insights queries
- [ ] Configure SNS topics for alerts
- [ ] Set up AWS Budgets for cost monitoring

### 4. Security Hardening

- [ ] Review IAM policies
- [ ] Enable AWS GuardDuty
- [ ] Enable AWS Config
- [ ] Set up AWS WAF rules (if needed)
- [ ] Review security group rules
- [ ] Enable MFA on AWS account

### 5. Cost Optimization

- [ ] Review AWS Cost Explorer
- [ ] Set up billing alerts
- [ ] Enable AWS Compute Optimizer
- [ ] Review S3 lifecycle policies
- [ ] Consider Reserved Instances/Savings Plans

## Rollback Plan

If issues arise during migration:

### 1. Quick Rollback to Docker Compose

```bash
# Start old environment
docker-compose up -d

# Restore database (if needed)
docker exec -i postgres psql -U postgres smartglasses < smartglasses_backup.sql

# Restore MinIO data (if needed)
./mc mirror ./minio-backup/media local/smart-glasses-media
./mc mirror ./minio-backup/documents local/smart-glasses-documents
```

### 2. Partial Rollback

Keep AWS infrastructure but revert application:

```bash
# Rollback ECS service to previous task definition
aws ecs update-service \
  --cluster smart-glasses-dev-cluster \
  --service smart-glasses-dev-api-service \
  --task-definition smart-glasses-dev-api:PREVIOUS_VERSION
```

## Troubleshooting

### Issue: Database Connection Timeouts

**Solution**: Check security groups and VPC configuration

```bash
# Verify security group allows traffic from ECS
aws ec2 describe-security-groups \
  --group-ids $(terraform output -raw rds_security_group_id)
```

### Issue: Lambda Function Errors

**Solution**: Check CloudWatch logs and environment variables

```bash
# View detailed logs
aws logs tail /aws/lambda/smart-glasses-dev-document-processor --follow

# Check function configuration
aws lambda get-function-configuration \
  --function-name smart-glasses-dev-document-processor
```

### Issue: High Costs

**Solution**: Review CloudWatch metrics and optimize

```bash
# Check cost and usage
aws ce get-cost-and-usage \
  --time-period Start=2024-01-01,End=2024-01-31 \
  --granularity MONTHLY \
  --metrics BlendedCost \
  --group-by Type=SERVICE
```

## Support

For migration assistance:
- AWS Support (if you have a support plan)
- AWS Documentation: https://docs.aws.amazon.com/
- GitHub Issues: Open an issue in the repository
- Community: AWS Community Forums

## Conclusion

After completing this migration, your Smart Glasses Middleware will be:
- ✅ Running on scalable AWS infrastructure
- ✅ Using managed services (RDS, ElastiCache, Bedrock)
- ✅ Automatically scaling based on load
- ✅ Secured with AWS security best practices
- ✅ Monitored with CloudWatch
- ✅ Cost-optimized with auto-scaling and lifecycle policies

**Next Steps**: Monitor the application for a few days, optimize based on metrics, and gradually increase traffic to production levels.
