# GitHub Actions Runner Storage Optimization

## Overview
The deployment workflow has been optimized to prevent runner storage exhaustion by implementing aggressive cleanup of Docker images, build caches, and temporary files.

## Problem Addressed
GitHub reported that the runner storage quota for the month was exceeded due to accumulation of:
- Docker images not cleaned up after ECR push
- Docker buildx layer caches
- Node module dependencies
- Python package caches
- Terraform state working directories

## Solution Implemented

### Cleanup Steps Added
The workflow now includes 4 cleanup jobs executed with `if: always()` to ensure they run regardless of previous step success/failure:

#### 1. Docker & Buildx Cleanup (build-api job)
- Executes after pushing API image to ECR
- Commands:
  - `docker system prune -af --volumes` - Remove dangling images and containers
  - `docker buildx prune -af` - Clear buildx cache
  - Cleanup buildx cache directories in /tmp

#### 2. Python Cache Cleanup (build-lambda job)
- Executes after building Lambda deployment package
- Commands:
  - `rm -rf ~/.cache/pip ~/.cache/python` - Remove pip/Python caches
  - Remove all `__pycache__` directories
  - Remove pytest cache

#### 3. Final Storage Cleanup (terraform-apply job)
- Executes after infrastructure deployment
- Commands:
  - Remove Terraform working directories (`.terraform/`, `.tfvars`)
  - Remove Lambda deployment zip
  - Full Docker system cleanup (again)
  - Remove node_modules from frontend builds
  - Remove dotnet and nuget caches
  - Clear all temp files in `/tmp/` and `/var/tmp/`
  - Report final disk usage with `df -h`

#### 4. Frontend Build Artifact Cleanup (deploy-frontend job)
- Executes after deploying frontend to S3
- Commands:
  - Remove node_modules directory
  - Remove Next.js build cache (`.next/`)
  - Clear npm cache

## Expected Impact
- **Storage Saved Per Run**: ~3-5GB (Docker images + node_modules + caches)
- **With 20 deployments/month**: 60-100GB freed
- **Result**: Prevents monthly storage quota exhaustion

## Implementation Details
- All cleanup steps use shell scripts with `|| true` to continue on error
- `if: always()` condition ensures cleanup runs even if build fails
- Cleanup steps are non-blocking and don't affect deployment success
- Disk usage is reported after cleanup for monitoring

## Files Changed
- `.github/workflows/deploy-main.yml`
  - Added ~53 lines across 4 cleanup jobs
  - 2 commits: b42afa5, 9140d80

## Testing
Run a test deployment with:
```bash
gh workflow run deploy-main.yml -f environment=dev -f skip_migrations=true -f force_recreate_dev=false
```

Monitor the run to verify cleanup steps execute and report disk usage.

## Future Improvements
- Consider using GitHub's official cache management actions
- Implement S3 cache backend for Docker buildx
- Add storage usage metrics to GitHub deployment summaries
