output "vpc_id" {
  description = "VPC ID"
  value       = aws_vpc.main.id
}

output "rds_endpoint" {
  description = "RDS PostgreSQL endpoint"
  value       = local.rds_endpoint
}

output "elasticache_endpoint" {
  description = "ElastiCache Redis endpoint"
  value       = local.enable_redis_effective ? (var.environment == "dev" ? aws_elasticache_cluster.redis_single[0].cache_nodes[0].address : aws_elasticache_replication_group.redis[0].primary_endpoint_address) : null
}

output "s3_media_bucket" {
  description = "S3 bucket for media storage"
  value       = aws_s3_bucket.media.id
}

output "s3_documents_bucket" {
  description = "S3 bucket for document storage"
  value       = aws_s3_bucket.documents.id
}

output "cloudfront_distribution_domain" {
  description = "CloudFront distribution domain name"
  value       = aws_cloudfront_distribution.frontend.domain_name
}

output "website_url" {
  description = "Website URL (custom domain if configured, otherwise CloudFront domain)"
  value       = var.domain_name != "" ? "https://${var.domain_name}" : "https://${aws_cloudfront_distribution.frontend.domain_name}"
}

output "api_alb_dns" {
  description = "API Application Load Balancer DNS name"
  value       = aws_lb.api.dns_name
}

output "ecs_cluster_name" {
  description = "ECS cluster name"
  value       = aws_ecs_cluster.main.name
}

output "lambda_document_processor_arn" {
  description = "Lambda function ARN for document processor"
  value       = aws_lambda_function.document_processor.arn
}

output "bedrock_model_id" {
  description = "Bedrock model ID for LLM"
  value       = "anthropic.claude-3-sonnet-20240229-v1:0"
}

output "frontend_bucket_name" {
  description = "S3 bucket name for the frontend static files"
  value       = aws_s3_bucket.frontend.id
}

output "cloudfront_distribution_id" {
  description = "CloudFront distribution ID (used for cache invalidation)"
  value       = aws_cloudfront_distribution.frontend.id
}

output "secrets_manager_arn" {
  description = "Secrets Manager secret ARN"
  value       = aws_secretsmanager_secret.app_secrets.arn
}
