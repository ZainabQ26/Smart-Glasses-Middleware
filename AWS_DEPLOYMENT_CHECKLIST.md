# AWS Deployment Checklist

Use this checklist to track your deployment progress.

## Phase 1: AWS Setup
- [ ] AWS CLI installed and configured (`aws configure`)
- [ ] Verified AWS credentials (`aws sts get-caller-identity`)
- [ ] Created Terraform state bucket
- [ ] Updated Terraform backend in `aws/infrastructure/main.tf`
- [ ] Initialized Terraform (`terraform init`)

## Phase 2: Infrastructure
- [ ] Created `terraform.tfvars` with your settings
- [ ] Reviewed Terraform plan (`terraform plan`)
- [ ] Applied infrastructure (`terraform apply`)
- [ ] Saved Terraform outputs to file
- [ ] Noted ECR repository URLs
- [ ] Noted RDS endpoint
- [ ] Noted ALB DNS name
- [ ] Noted SQS queue URL

## Phase 3: Database
- [ ] Enabled pgvector extension on RDS
- [ ] Retrieved DB credentials from Secrets Manager
- [ ] Ran EF Core migrations (`dotnet ef database update`)
- [ ] Verified `TranscriptionJobs` table exists

## Phase 4: Docker Images
- [ ] Logged into ECR (`aws ecr get-login-password`)
- [ ] Built .NET API Docker image
- [ ] Pushed API image to ECR
- [ ] Built Whisper Lambda Docker image
- [ ] Pushed Whisper image to ECR
- [ ] Updated `terraform.tfvars` with Whisper image URI
- [ ] Re-applied Terraform

## Phase 5: ECS Deployment
- [ ] Forced ECS service update
- [ ] Waited for service to stabilize
- [ ] Tested `/health` endpoint
- [ ] Verified ECS tasks are running

## Phase 6: Configuration
- [ ] Updated Secrets Manager with app config
- [ ] Added S3 bucket name to environment
- [ ] Added Whisper Lambda function name
- [ ] Added SQS queue URL
- [ ] Added Redis endpoint
- [ ] Re-deployed ECS service

## Phase 7: Client App (Optional)
### Option A: S3 + CloudFront
- [ ] Built client app (`npm run build`)
- [ ] Uploaded to S3 bucket
- [ ] Invalidated CloudFront cache
- [ ] Tested CloudFront URL

### Option B: Serve from .NET
- [ ] Built client app
- [ ] Copied dist/ to wwwroot/
- [ ] Rebuilt and pushed API image

## Phase 8: Testing
- [ ] Tested `/health` endpoint
- [ ] Tested document upload API
- [ ] Tested chat API
- [ ] Tested Whisper Lambda directly
- [ ] Checked ECS logs in CloudWatch
- [ ] Checked Lambda logs in CloudWatch

## Phase 9: Custom Domain (Optional)
- [ ] Requested ACM certificate
- [ ] Added DNS validation records
- [ ] Waited for certificate validation
- [ ] Updated Terraform with domain and certificate
- [ ] Applied Terraform
- [ ] Created CNAME record pointing to ALB
- [ ] Tested custom domain

## Phase 10: Production Hardening (Optional)
- [ ] Enabled ECS auto-scaling
- [ ] Configured S3 lifecycle policies
- [ ] Set up CloudWatch alarms
- [ ] Configured WAF rules
- [ ] Set up backup policies
- [ ] Documented disaster recovery plan

## Post-Deployment
- [ ] Documented all endpoints and credentials
- [ ] Set up monitoring dashboard
- [ ] Configured alerting channels (email/Slack)
- [ ] Created runbook for common issues
- [ ] Scheduled regular backups
- [ ] Set up cost monitoring alerts

## Verification Commands

```powershell
# Check ECS service status
aws ecs describe-services --cluster smart-glasses-prod --services smart-glasses-prod-api

# Check API health
$albDns = terraform output -raw -state=aws/infrastructure/terraform.tfstate alb_dns_name
curl "http://$albDns/health"

# Check Lambda status
aws lambda get-function --function-name smart-glasses-whisper

# Check RDS status
aws rds describe-db-clusters --db-cluster-identifier smart-glasses-prod

# Check ElastiCache status
aws elasticache describe-cache-clusters --cache-cluster-id smart-glasses-prod-redis

# View recent logs
aws logs tail /ecs/smart-glasses-prod-api --follow
aws logs tail /aws/lambda/smart-glasses-whisper --follow

# Check costs (current month)
aws ce get-cost-and-usage `
  --time-period Start=2026-02-01,End=2026-02-28 `
  --granularity MONTHLY `
  --metrics UnblendedCost `
  --group-by Type=SERVICE
```

## Troubleshooting Quick Reference

| Issue | Command to Check | Fix |
|-------|------------------|-----|
| ECS tasks not starting | `aws ecs describe-tasks ...` | Check logs, verify secrets, run migrations |
| API 502 errors | `aws logs tail /ecs/...` | Check app logs, verify DB connection |
| Lambda timeout | `aws logs tail /aws/lambda/...` | Increase memory/timeout, check S3 access |
| Can't connect to RDS | Check security groups | Update RDS SG to allow ECS SG on port 5432 |
| High costs | `aws ce get-cost-and-usage` | Review RDS sizing, disable NAT if unused |

## Rollback

If deployment fails and you need to start over:

```powershell
# Destroy all infrastructure
cd aws/infrastructure
terraform destroy

# Delete ECR images
aws ecr batch-delete-image --repository-name smart-glasses-prod-api --image-ids imageTag=latest
aws ecr batch-delete-image --repository-name smart-glasses-prod-whisper --image-ids imageTag=latest

# Delete Terraform state bucket (if needed)
aws s3 rb s3://smart-glasses-tfstate-XXXXX --force
```

## Success Criteria

Your deployment is successful when:

- ✅ `/health` returns JSON with all checks healthy
- ✅ Can upload a document via API
- ✅ Can send a chat message and get AI response
- ✅ Can record audio and get transcription (via Whisper Lambda)
- ✅ Client app loads and displays system status
- ✅ ECS service has 2+ running tasks
- ✅ No errors in CloudWatch logs
- ✅ SQS messages are being processed
- ✅ Database has data (check `TranscriptionJobs` table)

**Estimated total time: 2-3 hours for first deployment**

---

**Next:** See [AWS_DEPLOYMENT_GUIDE.md](AWS_DEPLOYMENT_GUIDE.md) for detailed instructions on each phase.
