variable "aws_region" {
  description = "AWS region for resources"
  type        = string
  default     = "us-east-1"
}

variable "cost_optimized_non_prod" {
  description = "When true, non-prod defaults favor minimum idle cost (ECS min/desired=0, Redis off, and reduced telemetry)."
  type        = bool
  default     = true
}

variable "enable_customer_managed_kms" {
  description = "Enable customer-managed KMS keys for S3/RDS/ECR encryption. Set null to use environment-aware defaults."
  type        = bool
  default     = null
  nullable    = true
}

variable "enable_s3_versioning" {
  description = "Enable S3 bucket versioning for media/documents/frontend buckets. Set null to use environment-aware defaults."
  type        = bool
  default     = null
  nullable    = true
}

variable "dev_public_network_mode" {
  description = "When true for non-prod, run ECS API tasks in public subnets with public IPs (cheaper than private+interface endpoints, but lower network isolation)."
  type        = bool
  default     = false
}

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
  default     = "dev"
}

variable "project_name" {
  description = "Project name for resource naming"
  type        = string
  default     = "smart-glasses"
}

variable "s3_bucket_suffix" {
  description = "Optional suffix appended to S3 bucket names for global uniqueness (for example, AWS account ID)."
  type        = string
  default     = ""
}

variable "vpc_cidr" {
  description = "CIDR block for VPC"
  type        = string
  default     = "10.0.0.0/16"
}

variable "availability_zones" {
  description = "Availability zones for multi-AZ deployment"
  type        = list(string)
  default     = ["us-east-1a", "us-east-1b"]
}

variable "ecs_api_cpu" {
  description = "CPU units for API ECS task"
  type        = number
  default     = 1024  # 1 vCPU (up from 0.5); halves .NET JIT / EF migration time on cold start
}

variable "ecs_api_memory" {
  description = "Memory (MiB) for API ECS task"
  type        = number
  default     = 2048  # Fargate minimum for 1024 CPU (1 vCPU) is 2048 MiB
}

variable "ecs_api_desired_count" {
  description = "Desired number of API ECS tasks"
  type        = number
  default     = 1
}

variable "ecs_api_min_capacity" {
  description = "Minimum ECS desired count for autoscaling"
  type        = number
  default     = 1
}

variable "ecs_api_max_capacity" {
  description = "Maximum ECS desired count for autoscaling"
  type        = number
  default     = 1
}

variable "enable_container_insights" {
  description = "Enable ECS Container Insights. Set null to use environment-aware defaults."
  type        = bool
  default     = null
  nullable    = true
}

variable "enable_nat_gateway" {
  description = "Enable NAT Gateway for private subnets"
  type        = bool
  default     = false
}

variable "enable_vpc_interface_endpoints" {
  description = "Enable interface VPC endpoints (ECR, Logs, Secrets Manager). Set null to use environment-aware defaults."
  type        = bool
  default     = null
  nullable    = true
}

variable "enable_ecs_idle_autoscaler" {
  description = "Enable CloudWatch/EventBridge/Lambda based wake-on-traffic and idle scale-to-zero for ECS API service."
  type        = bool
  default     = false
}

variable "ecs_warm_desired_count" {
  description = "Desired ECS task count to set when traffic is detected and service is sleeping."
  type        = number
  default     = 1
}

variable "ecs_idle_timeout_minutes" {
  description = "Minutes of zero ALB requests before scaling ECS API service down to 0."
  type        = number
  default     = 15
}

variable "alb_idle_timeout_seconds" {
  description = "ALB idle timeout in seconds. Increase to reduce 504/connection close on slow origin responses."
  type        = number
  default     = 180
}

variable "domain_name" {
  description = "Domain name for CloudFront distribution (optional)"
  type        = string
  default     = ""
}

variable "certificate_arn" {
  description = "ACM certificate ARN for CloudFront (optional)"
  type        = string
  default     = ""
}

variable "api_image" {
  description = "Optional: full image URI (including tag) for the API container. If empty, uses the ECR repo :latest tag."
  type        = string
  default     = ""
}


variable "use_localstack" {
  description = "When true, configure the AWS provider to use LocalStack endpoints."
  type        = bool
  default     = false
}

variable "localstack_endpoint" {
  description = "Base endpoint URL for LocalStack (including scheme and port), e.g. http://host.docker.internal:4566 or http://localhost:4566"
  type        = string
  default     = "http://localhost:4566"
}

variable "deployment_id" {
  description = "Unique deployment ID (e.g. from GitHub Actions run ID) to force container updates"
  type        = string
  default     = ""
}

variable "groq_api_key" {
  description = "Groq API key injected into ECS as AI__Groq__ApiKey"
  type        = string
  default     = ""
  sensitive   = true
}

variable "enable_redis" {
  description = "Deprecated compatibility variable. Redis is currently not provisioned by this root module."
  type        = bool
  default     = false
}

variable "enable_rds_performance_insights" {
  description = "Deprecated compatibility variable retained for CI inputs."
  type        = bool
  default     = false
}

variable "rds_monitoring_interval" {
  description = "Deprecated compatibility variable retained for CI inputs (0 disables enhanced monitoring)."
  type        = number
  default     = 0
}

variable "db_username" {
  description = "PostgreSQL database username"
  type        = string
  default     = "postgres"
  sensitive   = true
}

variable "db_password" {
  description = "PostgreSQL database password"
  type        = string
  sensitive   = true
  default     = ""
}

variable "db_host" {
  description = "PostgreSQL database host"
  type        = string
  default     = ""
}

variable "db_port" {
  description = "PostgreSQL database port"
  type        = number
  default     = 5432
}

variable "db_name" {
  description = "PostgreSQL database name"
  type        = string
  default     = "smart_glasses"
}

variable "lambda_deployment_s3_bucket" {
  description = "S3 bucket that holds the Lambda deployment zip (avoids the 50 MB direct-upload limit)."
  type        = string
  default     = ""
}

variable "lambda_deployment_s3_key" {
  description = "S3 key (path) of the Lambda deployment zip inside lambda_deployment_s3_bucket."
  type        = string
  default     = ""
}

variable "lambda_deployment_hash" {
  description = "Base64-encoded SHA-256 of the Lambda deployment zip. Used as source_code_hash so Terraform detects code changes when the S3 key is fixed."
  type        = string
  default     = ""
}
