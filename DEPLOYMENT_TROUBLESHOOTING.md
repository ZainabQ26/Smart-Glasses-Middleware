# Deployment Troubleshooting Guide

## Current Status (Run 21)
**Workflow Status**: ❌ FAILED
**Latest Run**: #21 (Simplify terraform import - focus on critical resources only)

## Problem Summary

Terraform is attempting to create AWS resources that already exist in the `us-east-1` region. The `terraform import` commands have been unable to successfully adopt these pre-existing resources into the Terraform state.

### Existing Resources Causing Conflicts

1. **S3 Buckets** (successfully imported in most runs)
   - `smart-glasses-dev-media`
   - `smart-glasses-dev-documents`
   - `smart-glasses-dev-frontend`

2. **KMS Keys & Aliases** (mostly successful)
   - `alias/smart-glasses-dev-s3`
   - `alias/smart-glasses-dev-rds`

3. **Secrets Manager** (partially successful)
   - `smart-glasses-dev-app-secrets`
   - `smart-glasses-dev-db-credentials`
   - `smart-glasses-dev-redis-credentials`

4. **Database Resources** (NOT being imported - causing failures)
   - DB Subnet Group: `smart-glasses-dev-db-subnet-group`
   - RDS Cluster Parameter Group: `smart-glasses-dev-aurora-pg-params`
   - RDS Cluster, Instances, (multi-AZ)
   - RDS Monitoring Role

5. **Lambda & IAM Resources** (NOT being imported)
   - Lambda Functions: `smart-glasses-dev-whisper`, `smart-glasses-dev-document-processor`
   - IAM Policies: `smart-glasses-whisper-s3-access`, `smart-glasses-whisper-allow-sqs-send`
   - IAM Roles: `smart-glasses-whisper-exec`, `smart-glasses-document-exec`

6. **Load Balancer** (NOT being imported)
   - ALB: `smart-glasses-dev-api-alb`
   - Target Group: `smart-glasses-dev-api-tg`

7. **CloudWatch Log Groups** (NOT being imported)
   - Multiple log groups for ECS,Lambda, ElastiCache

8. **ECS Resources** (NOT being imported)
   - ECS Cluster: `smart-glasses-dev-cluster`

## Root Cause Analysis

The main issues are:

1. **Terraform import failures**: The `terraform import` commands are running with error suppression (`|| true`), which means when they fail, the failures are silently ignored.

2. **Pre-existing infrastructure**: The dev environment was deployed/configured before this Terraform-based CI/CD was set up, so AWS resources exist outside of Terraform's state.

3. **State mismatch**: Terraform state either doesn't exist or is incomplete, so it doesn't know which resources are already managed.

4. **Complex resource dependencies**: Some resources have circular dependencies or complex references that make import ordering tricky.

## Solution Options

### Option 1: Manual Terraform Import (Recommended for Production Data)
If you want to preserve all existing data, manually import each resource into the Terraform state:

```bash
cd aws/infrastructure
terraform init

# Import S3 buckets
terraform import aws_s3_bucket.media smart-glasses-dev-media
terraform import aws_s3_bucket.documents smart-glasses-dev-documents
terraform import aws_s3_bucket.frontend smart-glasses-dev-frontend

# Import RDS resources
terraform import aws_db_subnet_group.main smart-glasses-dev-db-subnet-group
terraform import aws_rds_cluster_parameter_group.main smart-glasses-dev-aurora-pg-params
terraform import aws_rds_cluster.main smart-glasses-dev-cluster
terraform import aws_rds_cluster_instance.main-0 smart-glasses-dev-cluster-instance-0
terraform import aws_rds_cluster_instance.main-1 smart-glasses-dev-cluster-instance-1

# ... and so on for other resources
```

**Pros**: Preserves all existing data
**Cons**: Time-consuming, requires manual steps for each resource

### Option 2: Fresh Deployment in New Environment (Cleanest)
Deploy to a new environment suffix (e.g., `-staging` instead of `-dev`):

```bash
# Update variables.tf or pass via CLI
terraform apply -var="environment=staging"
```

**Pros**: Clean slate, no conflicts, Terraform manages everything from start
**Cons**: Requires migrating data if needed, new infrastructure costs

### Option 3: Destroy & Recreate (FOR DEVELOPMENT ONLY)
If this is a development environment with no critical data:

```bash
# WARNING: This will delete all infrastructure
terraform destroy -auto-approve

# Then apply fresh
terraform apply -auto-approve
```

**Pros**: Simplest, guaranteed to work
**Cons**: **WILL DELETE ALL DATA** (databases, S3 buckets, etc.)

### Option 4: Partial Manual Cleanup
Delete only the resources causing the immediate failures, letting Terraform recreate them:

```bash
# Delete resources that are failing to import (lowest priority first)
aws ecs delete-cluster --cluster-name smart-glasses-dev-cluster --force || true
aws lambda delete-function --function-name smart-glasses-dev-whisper || true
aws elasticloadbalancing delete-load-balancer --load-balancer-arn <arn> || true
# ... etc
```

**Pros**: Selective deletion, preserves important data
**Cons**: Risk of leaving orphaned resources, complex dependencies

## Recommended Path Forward

**For immediate progress with safety**:

1. **Test locally first** - Run terraform import commands manually in your local environment
2. **Verify state** - Check `terraform.tfstate` to see which resources are already imported
3. **Use Option 2** if data is not critical - deploy to `-staging` environment
4. **Use Option 1** if you need to preserve the dev environment - manually import key resources

## Files Modified for Safety

The following files have been updated with lifecycle rules to prevent accidental data loss:

- `aws/infrastructure/s3.tf` - `prevent_destroy = true` on buckets and KMS keys
- `aws/infrastructure/rds.tf` - `prevent_destroy = true` on RDS cluster and `ignore_changes` on password
- `aws/infrastructure/secrets.tf` - `ignore_changes` on secret values
- `aws/infrastructure/elasticache.tf` - `prevent_destroy = true` on log groups
- `aws/infrastructure/lambda.tf`, `whisper_lambda.tf` - `prevent_destroy = true` on resources
- `aws/infrastructure/ecs.tf` - `prevent_destroy = true` on cluster and log groups

These rules will prevent Terraform from destroying critical resources even if configuration errors occur.

##  Next Steps

1. Decide which solution option you prefer
2. Implement the solution locally or via updated CI/CD
3. Update this document with the chosen approach
4. Proceed with deployment

For more information about terraform import, see: https://www.terraform.io/cli/commands/import
