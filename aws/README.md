# AWS Migration Architecture

This directory contains all AWS-specific code, infrastructure-as-code, and deployment resources for the Smart Glasses Middleware.

## Directory Structure

```
aws/
├── infrastructure/        # Terraform IaC for AWS resources
│   ├── main.tf           # Provider and backend configuration
│   ├── variables.tf      # Input variables
│   ├── outputs.tf        # Output values
│   ├── vpc.tf            # VPC and networking
│   ├── rds.tf            # Aurora PostgreSQL with pgvector
│   ├── elasticache.tf    # Redis cluster
│   ├── s3.tf             # S3 buckets
│   ├── ecs.tf            # ECS cluster and services
│   ├── alb.tf            # Application Load Balancer
│   ├── cloudfront.tf     # CloudFront distribution
│   ├── lambda.tf         # Lambda functions
│   ├── ecr.tf            # Container registry
│   ├── secrets.tf        # Secrets Manager
│   └── security-groups.tf# Security groups
├── lambda-document-processor/  # Document processing Lambda
│   ├── index.py          # Lambda handler
│   ├── requirements.txt  # Python dependencies
│   ├── build.sh          # Build script
│   └── README.md         # Documentation
├── ecs-api/              # API container for ECS
│   ├── Dockerfile        # Multi-stage Docker build
│   └── README.md         # Documentation
├── frontend/             # Frontend deployment (future)
├── scripts/              # Deployment and utility scripts
└── DEPLOYMENT.md         # Comprehensive deployment guide
```

## Architecture Overview

### Original Architecture (Docker Compose)
- **MinIO**: S3-compatible object storage
- **Ollama**: Local LLM inference
- **PostgreSQL**: Database with pgvector
- **Redis**: Cache and pub/sub
- **Vosk**: Speech-to-text service
- **API**: .NET 10 Web API

### AWS Architecture
- **Amazon S3**: Object storage for media and documents
- **Amazon Bedrock**: Managed LLM service (Claude 3 Sonnet)
- **Amazon RDS Aurora**: Serverless PostgreSQL with pgvector
- **Amazon ElastiCache**: Managed Redis
- **Whisper Lambda**: Self-hosted Whisper model in Lambda for speech-to-text (replaces AWS Transcribe)
- **AWS Lambda**: Serverless document processing
- **Amazon ECS Fargate**: Container orchestration for API
- **Application Load Balancer**: Load balancing and SSL
- **Amazon CloudFront**: CDN for frontend and API
- **AWS Secrets Manager**: Secure credential storage
- **Amazon ECR**: Docker image registry

## Key Changes

### 1. Storage: MinIO → S3
- **Implementation**: `S3MediaStorageService.cs`
- **Benefits**: 
  - Automatic scaling
  - 99.999999999% durability
  - Lifecycle policies for cost optimization
  - Integrated with CloudFront for fast delivery

### 2. AI: Ollama → Bedrock
- **Implementation**: `BedrockAIService.cs`
- **Model**: Claude 3 Sonnet (anthropic.claude-3-sonnet-20240229-v1:0)
- **Embeddings**: Titan Embeddings V2 (384 dimensions)
- **Benefits**:
  - No model management
  - Auto-scaling
  - Multiple model options
  - Pay-per-use pricing

### 3. Database: PostgreSQL → Aurora
- **Engine**: Aurora PostgreSQL 16 Serverless v2
- **Extension**: pgvector for vector similarity search
- **Benefits**:
  - Auto-scaling (0.5-2 ACU)
  - Multi-AZ high availability
  - Automated backups
  - Performance Insights
  - Minimal management

### 4. Cache: Redis → ElastiCache
- **Engine**: Redis 7.1
- **Mode**: Cluster mode with automatic failover
- **Benefits**:
  - Managed service
  - Multi-AZ replication
  - Auto-failover
  - Encryption at rest and in transit

### 5. Document Processing: Background Service → Lambda
- **Language**: Python 3.12 (more efficient for document processing)
- **Trigger**: S3 event notifications
- **Services**: Textract, Bedrock, Comprehend
- **Benefits**:
  - Serverless, no idle costs
  - Auto-scaling
  - Parallel processing
  - Faster execution with AWS services

### 6. Transcription: Vosk → Transcribe
- **Implementation**: `AwsTranscribeService.cs`
- **Alternative**: Self-hosted Vosk on ECS for cost control
- **Benefits**:
  - No model management
  - High accuracy
  - Multiple language support
  - Pay-per-use

### 7. API Deployment: Docker Compose → ECS Fargate
- **Container**: .NET 10 runtime
- **Orchestration**: ECS with Fargate (serverless containers)
- **Benefits**:
  - No server management
  - Auto-scaling based on CPU/memory
  - Integrated with ALB
  - Blue/green deployments

### 8. Frontend: Static Files → S3 + CloudFront
- **Storage**: S3 bucket configured for static website hosting
- **CDN**: CloudFront with global edge locations
- **Benefits**:
  - Ultra-low latency
  - Automatic SSL
  - DDoS protection
  - Cost-effective

## Cost Optimization

### Strategies Implemented
1. **Aurora Serverless v2**: Scales down to 0.5 ACU when idle
2. **S3 Lifecycle Policies**: Auto-archive to Glacier after 90/180 days
3. **Fargate Spot**: Option for non-production workloads
4. **VPC Endpoints**: Reduce NAT Gateway costs
5. **CloudWatch Log Retention**: 7-30 days instead of indefinite
6. **Lambda**: Pay only for execution time
7. **CloudFront**: Reduce origin requests with caching

### Estimated Monthly Costs (Dev Environment)
- **RDS Aurora**: $50-100 (0.5-2 ACU)
- **ElastiCache**: $15-30 (t3.micro)
- **ECS Fargate**: $40-80 (2 tasks, 1 vCPU, 2GB)
- **S3**: $5-20 (depends on usage)
- **Bedrock**: $0.01-0.03 per 1000 tokens
- **Lambda**: $0.20 per 1M requests + compute
- **CloudFront**: $0.085 per GB (first 10TB)
- **Data Transfer**: Variable
- **Total**: ~$150-300/month

### Production Costs
- Scale linearly with traffic
- Enable Reserved Instances for 30-50% savings
- Use Savings Plans for Fargate
- Consider Aurora I/O-Optimized for high workloads

## Testing with LocalStack

LocalStack allows you to test AWS services locally without incurring costs.

### Supported Services
- S3
- Lambda
- Secrets Manager
- CloudWatch Logs
- Partial: Bedrock (mocked), Transcribe (mocked)

### Usage
```bash
# Start LocalStack environment
docker-compose -f docker-compose.localstack.yml up -d

# Run tests
dotnet test

# Run E2E tests
npm test
```

## Deployment

See [DEPLOYMENT.md](./DEPLOYMENT.md) for comprehensive deployment instructions.

### Quick Start
```bash
# 1. Configure Terraform backend
cd aws/infrastructure
terraform init

# 2. Deploy infrastructure
terraform apply

# 3. Build and push Docker image
docker build -t smart-glasses-api -f aws/ecs-api/Dockerfile .
# ... push to ECR

# 4. Deploy Lambda
cd aws/lambda-document-processor
./build.sh
aws lambda update-function-code ...

# 5. Run migrations
dotnet ef database update
```

### Automated Deployment
GitHub Actions workflow (`.github/workflows/deploy-aws.yml`) automatically:
1. Runs tests
2. Builds Docker image
3. Pushes to ECR
4. Deploys to ECS
5. Updates Lambda function
6. Runs E2E tests

## Security

### Implemented Practices
1. **Encryption**: All data encrypted at rest (KMS) and in transit (TLS)
2. **IAM Roles**: Least privilege access
3. **Secrets Manager**: No hardcoded credentials
4. **VPC**: Private subnets for compute/database
5. **Security Groups**: Restrictive ingress rules
6. **WAF**: CloudFront can integrate with AWS WAF
7. **GuardDuty**: Enable for threat detection
8. **Config**: Enable for compliance monitoring

### Recommendations
- Enable MFA on AWS account
- Use AWS SSO for team access
- Implement CloudTrail for audit logging
- Regular security reviews
- Automated vulnerability scanning

## Monitoring

### CloudWatch Integration
- **Logs**: All services send logs to CloudWatch
- **Metrics**: Custom metrics for API, Lambda, ECS
- **Alarms**: Auto-scaling triggers, error rate alerts
- **Dashboards**: Centralized monitoring

### Key Metrics
- API latency (P50, P95, P99)
- Error rates (4xx, 5xx)
- Database connections/queries
- Cache hit rate
- Lambda duration/errors
- S3 request counts

## Performance Optimizations

### Implemented
1. **Connection Pooling**: Database and Redis
2. **Async/Await**: Non-blocking I/O throughout
3. **Caching**: Redis for frequently accessed data
4. **CDN**: CloudFront for static and API caching
5. **Vector Indexing**: pgvector with proper indexes
6. **Batch Processing**: Lambda processes chunks in parallel
7. **Compression**: Brotli/Gzip for responses

### Future Optimizations
- GraphQL for efficient data fetching
- AWS AppSync for real-time updates
- DynamoDB for session storage
- ElastiCache DAX for database caching

## Migration Path

### Phase 1: Infrastructure (Current)
- ✅ Terraform IaC complete
- ✅ All AWS services defined
- ✅ Security and networking configured

### Phase 2: Code Updates
- ✅ S3 service implementation
- ✅ Bedrock AI service
- ✅ Whisper Lambda (transcription)
- 🔄 Integration with existing code
- ⏳ Update unit tests

### Phase 3: Testing
- 🔄 LocalStack integration
- ⏳ Unit tests updated
- ⏳ E2E tests updated
- ⏳ Load testing

### Phase 4: Deployment
- ⏳ Deploy to dev environment
- ⏳ Smoke tests
- ⏳ Deploy to staging
- ⏳ Full regression testing
- ⏳ Deploy to production

### Phase 5: Optimization
- ⏳ Performance tuning
- ⏳ Cost optimization
- ⏳ Security hardening
- ⏳ Documentation updates

## Contributing

When making changes to AWS infrastructure:

1. Update Terraform files in `infrastructure/`
2. Update service implementations in `src/SmartGlassesMiddleware/Services/AWS/`
3. Update tests to work with LocalStack
4. Update documentation
5. Test locally with LocalStack
6. Deploy to dev environment
7. Create pull request

## Support

For issues or questions:
- Check [DEPLOYMENT.md](./DEPLOYMENT.md) for deployment issues
- Review CloudWatch logs for runtime errors
- Check Terraform state for infrastructure issues
- Consult AWS documentation for service-specific questions

## License

Same as parent repository.
