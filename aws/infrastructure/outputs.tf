output "vpc_id" {
  description = "VPC ID"
  value       = aws_vpc.main.id
}

output "s3_media_bucket" {
  description = "S3 bucket for media storage"
  value       = aws_s3_bucket.media.id
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

output "ecs_wake_url" {
  description = "Lambda Function URL for direct ECS wake (called by WakeupGate via /wake CF behavior)"
  value       = local.enable_ecs_idle_autoscaler_effective ? aws_lambda_function_url.ecs_idle_scaler[0].function_url : ""
}
