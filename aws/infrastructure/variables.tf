variable "aws_region" {
  description = "AWS region for resources"
  type        = string
  default     = "us-east-1"
}

variable "enable_rds" {
  description = "Kept for backward compatibility; RDS is always created (standard db.t3.micro, free-tier eligible)"
  type        = bool
  default     = true
}

variable "cost_optimized_non_prod" {
  description = "When true, non-prod defaults favor minimum idle cost (ECS min/desired=0, Redis off, and reduced telemetry)."
  type        = bool
  default     = true
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

variable "db_instance_class" {
  description = "RDS instance class (db.t3.micro = free tier eligible)"
  type        = string
  default     = "db.t3.micro"
}

variable "db_name" {
  description = "Database name"
  type        = string
  default     = "smartglasses"
}

variable "db_username" {
  description = "Database master username"
  type        = string
  default     = "postgres"
}

variable "elasticache_node_type" {
  description = "ElastiCache node type"
  type        = string
  default     = "cache.t3.micro"
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
  default     = 0
}

variable "ecs_api_min_capacity" {
  description = "Minimum ECS desired count for autoscaling"
  type        = number
  default     = 0
}

variable "ecs_api_max_capacity" {
  description = "Maximum ECS desired count for autoscaling"
  type        = number
  default     = 4
}

variable "enable_container_insights" {
  description = "Enable ECS Container Insights. Set null to use environment-aware defaults."
  type        = bool
  default     = null
  nullable    = true
}

variable "enable_redis" {
  description = "Enable Redis/ElastiCache resources. Set null to use environment-aware defaults."
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

variable "enable_rds_performance_insights" {
  description = "Enable RDS Performance Insights"
  type        = bool
  default     = false
}

variable "rds_monitoring_interval" {
  description = "RDS Enhanced Monitoring interval in seconds (0 disables)"
  type        = number
  default     = 0
}

variable "enable_ecs_idle_autoscaler" {
  description = "Enable CloudWatch/EventBridge/Lambda based wake-on-traffic and idle scale-to-zero for ECS API service."
  type        = bool
  default     = true
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

variable "lambda_image" {
  description = "Optional: full image URI (including tag) for the Whisper Lambda image. If empty, Terraform will expect a default or separate variable."
  type        = string
  default     = ""
}

variable "lambda_whisper_image" {
  description = "Full image URI for the Whisper Lambda Docker image"
  type        = string
  default     = ""
}

variable "lambda_document_image" {
  description = "Full image URI for the Document Processor Lambda (if packaged as image)."
  type        = string
  default     = ""
}

variable "client_image" {
  description = "Optional: full image URI (including tag) for the client container (optional)."
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
