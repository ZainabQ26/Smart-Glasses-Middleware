# AWS Deployment - Complete Step-by-Step Guide

Deploy Smart Glasses Middleware to AWS with backend API, Whisper Lambda, and optional client frontend.

## Prerequisites Checklist

Before starting, ensure you have:

- [ ] AWS Account with admin access (or permissions for ECS, RDS, S3, Lambda, ECR, VPC)
- [ ] AWS CLI v2 installed: `aws --version`
- [ ] Terraform ≥1.0 installed: `terraform --version`
- [ ] Docker installed and running: `docker --version`
- [ ] .NET SDK 10: `dotnet --version`
- [ ] Git: `git --version`
- [ ] Domain name (optional, for custom domain)
- [ ] ACM certificate (optional, for HTTPS)

---

## Phase 1: AWS Setup (15 minutes)

### Step 1.1: Configure AWS CLI

```powershell
# Configure AWS credentials
aws configure

# You'll be prompted for:
# AWS Access Key ID: <your-key-id>
# AWS Secret Access Key: <your-secret>
# Default region: us-east-1
# Default output format: json

# Test connection
aws sts get-caller-identity
```

**Expected output:**
```json
{
  "UserId": "AIDAI...",
  "Account": "123456789012",
  "Arn": "arn:aws:iam::123456789012:user/your-name"
}
```

### Step 1.2: Create Terraform State Bucket

```powershell
# Set your bucket name (must be globally unique)
$TF_STATE_BUCKET = "smart-glasses-tfstate-$(Get-Random)"

# Create bucket
aws s3 mb s3://$TF_STATE_BUCKET --region us-east-1

# Enable versioning
aws s3api put-bucket-versioning `
  --bucket $TF_STATE_BUCKET `
  --versioning-configuration Status=Enabled

# Enable encryption
aws s3api put-bucket-encryption `
  --bucket $TF_STATE_BUCKET `
  --server-side-encryption-configuration '{
    "Rules": [{"ApplyServerSideEncryptionByDefault": {"SSEAlgorithm": "AES256"}}]
  }'

Write-Host "Terraform state bucket created: $TF_STATE_BUCKET" -ForegroundColor Green
```

### Step 1.3: Configure Terraform Backend

Edit `aws/infrastructure/main.tf` and update the backend block:

```hcl
terraform {
  backend "s3" {
    bucket = "smart-glasses-tfstate-XXXXX"  # Replace with your bucket name
    key    = "smart-glasses/terraform.tfstate"
    region = "us-east-1"
    encrypt = true
  }
  
  required_version = ">= 1.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}
```

---

## Phase 2: Infrastructure Deployment (30-45 minutes)

### Step 2.1: Initialize Terraform

```powershell
cd aws\infrastructure

# Initialize Terraform (downloads providers)
terraform init
```

**Expected output:** `Terraform has been successfully initialized!`

### Step 2.2: Create Variables File

Create `terraform.tfvars` in `aws/infrastructure/`:

```hcl
# Required variables
aws_region   = "us-east-1"
environment  = "prod"
project_name = "smart-glasses"

# Infrastructure sizing (adjust for production)
db_instance_class       = "db.t3.medium"
elasticache_node_type   = "cache.t3.micro"
ecs_api_cpu             = 1024
ecs_api_memory          = 2048
ecs_api_desired_count   = 2

# Optional: Custom domain (requires ACM certificate)
# domain_name      = "api.yourdomain.com"
# certificate_arn  = "arn:aws:acm:us-east-1:123456789012:certificate/..."

# Whisper Lambda configuration
whisper_image_uri      = ""  # Will fill in after ECR push
whisper_function_name  = "smart-glasses-whisper"
lambda_memory_mb       = 2048
lambda_timeout         = 300

# Cost optimization (disable for production)
enable_nat_gateway = true  # Set false to save ~$30/month (breaks egress)
```

### Step 2.3: Plan Infrastructure

```powershell
# Preview what will be created
terraform plan -out=tfplan

# Review output carefully - should create:
# - VPC, subnets, NAT gateway, Internet gateway
# - RDS Aurora cluster (2 instances)
# - ElastiCache Redis cluster
# - S3 buckets (media, frontend)
# - ECR repositories (api, whisper)
# - ECS cluster, task definitions, service
# - ALB with target groups
# - Lambda function skeleton
# - IAM roles and security groups
```

### Step 2.4: Apply Infrastructure

```powershell
# Create all resources (takes 20-30 minutes)
terraform apply tfplan

# Confirm with: yes
```

**Wait for completion.** RDS and ElastiCache take longest.

### Step 2.5: Save Outputs

```powershell
# Save important outputs
terraform output > ..\..\terraform-outputs.txt

# Display key values
terraform output ecr_api_repository_url
terraform output ecr_whisper_repository_url
terraform output rds_endpoint
terraform output alb_dns_name
terraform output whisper_sqs_queue_url
```

---

## Phase 3: Database Setup (10 minutes)

### Step 3.1: Enable pgvector Extension

```powershell
# Get DB credentials from Secrets Manager
$dbSecret = aws secretsmanager get-secret-value `
  --secret-id smart-glasses-prod-db-credentials `
  --query SecretString --output text | ConvertFrom-Json

$dbHost = terraform output -raw rds_endpoint
$dbUser = $dbSecret.username
$dbPass = $dbSecret.password

# Connect with psql (requires PostgreSQL client installed)
# If you don't have psql, use AWS Systems Manager Session Manager or RDS Query Editor
$env:PGPASSWORD = $dbPass
psql -h $dbHost -U $dbUser -d smartglasses -c "CREATE EXTENSION IF NOT EXISTS vector;"

Write-Host "pgvector extension enabled" -ForegroundColor Green
```

**Alternative: Use DBeaver or pgAdmin to connect and run:**
```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

### Step 3.2: Run Database Migrations

```powershell
cd ..\..\src\SmartGlassesMiddleware

# Build connection string
$connString = "Host=$dbHost;Database=smartglasses;Username=$dbUser;Password=$dbPass;SSL Mode=Require"

# Run migrations
dotnet ef database update --connection $connString

Write-Host "Database migrations applied" -ForegroundColor Green
```

---

## Phase 4: Build and Push Docker Images (20 minutes)

### Step 4.1: Login to ECR

```powershell
cd ..\..  # Back to repo root

# Get ECR URLs from Terraform
$ecrApiRepo = terraform output -raw -state=aws/infrastructure/terraform.tfstate ecr_api_repository_url
$ecrWhisperRepo = terraform output -raw -state=aws/infrastructure/terraform.tfstate ecr_whisper_repository_url
$awsAccount = aws sts get-caller-identity --query Account --output text
$awsRegion = "us-east-1"

# Login to ECR
aws ecr get-login-password --region $awsRegion | `
  docker login --username AWS --password-stdin "$awsAccount.dkr.ecr.$awsRegion.amazonaws.com"

Write-Host "Logged into ECR" -ForegroundColor Green
```

### Step 4.2: Build and Push Backend API

```powershell
# Build .NET API image
docker build `
  -t smart-glasses-api:latest `
  -f src/SmartGlassesMiddleware/Dockerfile `
  .

# Tag for ECR
docker tag smart-glasses-api:latest $ecrApiRepo`:latest

# Push to ECR
docker push $ecrApiRepo`:latest

Write-Host "API image pushed to ECR: $ecrApiRepo" -ForegroundColor Green
```

### Step 4.3: Build and Push Whisper Lambda

```powershell
# Build Whisper Lambda container
cd aws\lambda-whisper

docker build -t smart-glasses-whisper:latest .

# Tag for ECR
docker tag smart-glasses-whisper:latest $ecrWhisperRepo`:latest

# Push to ECR
docker push $ecrWhisperRepo`:latest

Write-Host "Whisper Lambda image pushed to ECR: $ecrWhisperRepo" -ForegroundColor Green

cd ..\..
```

### Step 4.4: Update Terraform with Whisper Image URI

Edit `aws/infrastructure/terraform.tfvars` and add:

```hcl
whisper_image_uri = "<your-ecr-whisper-repo-url>:latest"
```

Then re-apply Terraform:

```powershell
cd aws\infrastructure
terraform apply -auto-approve
cd ..\..
```

---

## Phase 5: Deploy ECS Service (10 minutes)

### Step 5.1: Force ECS Service Update

```powershell
# Get ECS cluster and service names
$clusterName = "smart-glasses-prod"
$serviceName = "smart-glasses-prod-api"

# Force new deployment with latest image
aws ecs update-service `
  --cluster $clusterName `
  --service $serviceName `
  --force-new-deployment

Write-Host "ECS service deploying..." -ForegroundColor Yellow
```

### Step 5.2: Wait for Deployment

```powershell
# Monitor deployment status
aws ecs wait services-stable --cluster $clusterName --services $serviceName

Write-Host "ECS service is stable" -ForegroundColor Green
```

### Step 5.3: Verify API Health

```powershell
# Get ALB DNS name
$albDns = terraform output -raw -state=aws/infrastructure/terraform.tfstate alb_dns_name

# Test health endpoint
$healthUrl = "http://$albDns/health"
curl $healthUrl

# Expected: JSON with postgres, redis, minio health checks
```

---

## Phase 6: Configure Application Settings (15 minutes)

### Step 6.1: Update Secrets Manager

```powershell
# Get database and Redis connection strings
$dbEndpoint = terraform output -raw -state=aws/infrastructure/terraform.tfstate rds_endpoint
$redisEndpoint = terraform output -raw -state=aws/infrastructure/terraform.tfstate elasticache_endpoint

# Update application secrets
aws secretsmanager create-secret `
  --name smart-glasses-prod-app-config `
  --secret-string '{
    "AWS:Region": "us-east-1",
    "AWS:S3:MediaBucket": "smart-glasses-prod-media",
    "AWS:Transcription:UseWhisperLambda": "true",
    "AWS:Transcription:WhisperLambdaFunction": "smart-glasses-whisper",
    "AWS:Transcription:QueueUrl": "<from-terraform-output>",
    "AWS:Transcription:SyncThresholdBytes": "200000",
    "Redis:Configuration": "<redis-endpoint>:6379"
  }'
```

### Step 6.2: Update ECS Task Definition Environment

Edit `aws/infrastructure/ecs.tf` and add environment variables:

```hcl
environment = [
  { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
  { name = "AWS__Region", value = var.aws_region },
  { name = "AWS__S3__MediaBucket", value = aws_s3_bucket.media.id },
  { name = "AWS__Transcription__UseWhisperLambda", value = "true" },
  { name = "AWS__Transcription__WhisperLambdaFunction", value = aws_lambda_function.whisper.function_name },
  { name = "AWS__Transcription__QueueUrl", value = aws_sqs_queue.whisper_queue.url },
]
```

Then apply:

```powershell
cd aws\infrastructure
terraform apply -auto-approve
cd ..\..
```

---

## Phase 7: Deploy Client App (Optional, 15 minutes)

### Option A: Deploy to S3 + CloudFront

```powershell
cd client-app

# Install dependencies
npm install

# Build production bundle
npm run build

# Get S3 bucket name
$frontendBucket = terraform output -raw -state=../aws/infrastructure/terraform.tfstate frontend_bucket_name

# Upload to S3
aws s3 sync dist/ s3://$frontendBucket/ --delete

# Invalidate CloudFront cache
$distributionId = terraform output -raw -state=../aws/infrastructure/terraform.tfstate cloudfront_distribution_id
aws cloudfront create-invalidation --distribution-id $distributionId --paths "/*"

Write-Host "Client deployed to CloudFront" -ForegroundColor Green
```

### Option B: Serve from .NET Backend

```powershell
cd client-app
npm run build

# Copy to .NET wwwroot
Copy-Item -Path dist\* -Destination ..\src\SmartGlassesMiddleware\wwwroot\ -Recurse -Force

# Rebuild and push API image (repeat Step 4.2)
```

---

## Phase 8: Testing and Verification (10 minutes)

### Step 8.1: Test API Endpoints

```powershell
$apiUrl = "http://$albDns"

# Health check
curl "$apiUrl/health"

# Upload test document (requires form data)
curl -X POST "$apiUrl/api/documents/upload" `
  -F "file=@test.pdf" `
  -H "Content-Type: multipart/form-data"

# Test chat endpoint
curl -X POST "$apiUrl/api/chat/ask" `
  -H "Content-Type: application/json" `
  -d '{"prompt":"Hello, how are you?"}'
```

### Step 8.2: Test Whisper Lambda

```powershell
# Test Lambda directly
aws lambda invoke `
  --function-name smart-glasses-whisper `
  --payload '{"s3_bucket":"smart-glasses-prod-media","s3_key":"test.wav","job_id":"test-123"}' `
  response.json

cat response.json
```

### Step 8.3: Monitor Logs

```powershell
# API logs
aws logs tail /ecs/smart-glasses-prod-api --follow

# Lambda logs
aws logs tail /aws/lambda/smart-glasses-whisper --follow
```

---

## Phase 9: Custom Domain (Optional, 30 minutes)

### Step 9.1: Request ACM Certificate

```powershell
# Request certificate
aws acm request-certificate `
  --domain-name api.yourdomain.com `
  --validation-method DNS `
  --region us-east-1

# Note the CertificateArn from output
```

### Step 9.2: Add DNS Validation Records

Follow AWS Console instructions to add CNAME records to your DNS provider.

### Step 9.3: Update Terraform

Add to `terraform.tfvars`:

```hcl
domain_name     = "api.yourdomain.com"
certificate_arn = "arn:aws:acm:us-east-1:123456789012:certificate/..."
```

Apply:

```powershell
cd aws\infrastructure
terraform apply -auto-approve
```

### Step 9.4: Update DNS

Point your domain to ALB:

```powershell
# Get ALB DNS
$albDns = terraform output -raw alb_dns_name

# Create CNAME in your DNS provider:
# api.yourdomain.com -> <alb-dns>
```

---

## Phase 10: Production Hardening (Optional)

### Enable Auto-scaling

Edit `aws/infrastructure/ecs.tf`:

```hcl
resource "aws_appautoscaling_target" "api" {
  max_capacity       = 10
  min_capacity       = 2
  resource_id        = "service/${aws_ecs_cluster.main.name}/${aws_ecs_service.api.name}"
  scalable_dimension = "ecs:service:DesiredCount"
  service_namespace  = "ecs"
}

resource "aws_appautoscaling_policy" "cpu" {
  name               = "cpu-autoscaling"
  policy_type        = "TargetTrackingScaling"
  resource_id        = aws_appautoscaling_target.api.resource_id
  scalable_dimension = aws_appautoscaling_target.api.scalable_dimension
  service_namespace  = aws_appautoscaling_target.api.service_namespace

  target_tracking_scaling_policy_configuration {
    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }
    target_value = 70.0
  }
}
```

### Enable S3 Lifecycle Policies

```powershell
aws s3api put-bucket-lifecycle-configuration `
  --bucket smart-glasses-prod-media `
  --lifecycle-configuration '{
    "Rules": [{
      "Id": "delete-old-uploads",
      "Status": "Enabled",
      "Prefix": "transcribe-uploads/",
      "Expiration": {"Days": 7}
    }]
  }'
```

### Enable CloudWatch Alarms

```powershell
# High CPU alarm
aws cloudwatch put-metric-alarm `
  --alarm-name smart-glasses-high-cpu `
  --alarm-description "Alert when CPU exceeds 80%" `
  --metric-name CPUUtilization `
  --namespace AWS/ECS `
  --statistic Average `
  --period 300 `
  --threshold 80 `
  --comparison-operator GreaterThanThreshold `
  --dimensions Name=ServiceName,Value=smart-glasses-prod-api `
  --evaluation-periods 2
```

---

## Troubleshooting

### Issue: ECS tasks fail to start

**Check:**
```powershell
# Get task failures
aws ecs describe-tasks `
  --cluster smart-glasses-prod `
  --tasks (aws ecs list-tasks --cluster smart-glasses-prod --query 'taskArns[0]' --output text)
```

**Common causes:**
- Database migrations not run
- Secrets Manager permissions missing
- Docker image pull failed (check ECR permissions)

### Issue: Whisper Lambda timeout

**Increase timeout:**
```powershell
aws lambda update-function-configuration `
  --function-name smart-glasses-whisper `
  --timeout 600 `
  --memory-size 4096
```

### Issue: Can't connect to RDS

**Check security groups:**
```powershell
# Ensure ECS security group can reach RDS security group on port 5432
```

---

## Cost Estimate (Monthly, us-east-1)

| Service | Configuration | Estimated Cost |
|---------|--------------|----------------|
| RDS Aurora (2x t3.medium) | 24/7 | ~$85 |
| ElastiCache (t3.micro) | 24/7 | ~$15 |
| ECS Fargate (2 tasks, 1GB/1vCPU) | 24/7 | ~$60 |
| ALB | 24/7 + data transfer | ~$25 |
| NAT Gateway | 24/7 + data | ~$35 |
| S3 (100GB storage) | Standard | ~$2 |
| Lambda (Whisper, 1000 invokes/month) | 2GB, 30s avg | ~$10 |
| CloudWatch Logs (10GB/month) | Retention 7 days | ~$5 |
| **Total** | | **~$237/month** |

**Cost optimization tips:**
- Use RDS Aurora Serverless v2 (scales to zero)
- Use Lambda for API instead of ECS (pay per request)
- Disable NAT Gateway (if no egress needed)
- Use S3 Intelligent-Tiering

---

## Next Steps

1. ✅ API is live at `http://<alb-dns>`
2. ✅ Whisper Lambda is deployed
3. ✅ Client app (optional) is on CloudFront
4. Set up CI/CD with GitHub Actions
5. Configure monitoring and alerts
6. Set up backup and disaster recovery
7. Implement WAF for API protection

---

## Rollback

If anything goes wrong:

```powershell
# Destroy all infrastructure
cd aws\infrastructure
terraform destroy

# Confirm with: yes
```

This will delete all resources created by Terraform (except S3 buckets with objects - empty them first).

---

## Summary

You now have:
- ✅ .NET API running on ECS Fargate
- ✅ PostgreSQL database with pgvector
- ✅ Redis cache
- ✅ S3 storage
- ✅ Whisper Lambda for transcription
- ✅ SQS queue for async processing
- ✅ ALB with HTTPS
- ✅ CloudFront CDN (optional)
- ✅ Client app deployed (optional)

**Access your app:**
- API: `http://<alb-dns>/health`
- Client: `https://<cloudfront-domain>` or `http://<alb-dns>/`

**Congratulations! 🎉**
