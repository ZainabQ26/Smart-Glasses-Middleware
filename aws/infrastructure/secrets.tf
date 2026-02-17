# Application Secrets
resource "aws_secretsmanager_secret" "app_secrets" {
  name        = "${var.project_name}-${var.environment}-app-secrets"
  description = "Application secrets for Smart Glasses Middleware"

  tags = {
    Name = "${var.project_name}-${var.environment}-app-secrets"
  }
}

resource "aws_secretsmanager_secret_version" "app_secrets" {
  secret_id = aws_secretsmanager_secret.app_secrets.id
  secret_string = jsonencode({
    s3_media_bucket      = aws_s3_bucket.media.id
    s3_documents_bucket  = aws_s3_bucket.documents.id
    bedrock_model_id     = "anthropic.claude-3-sonnet-20240229-v1:0"
    embedding_model_id   = "amazon.titan-embed-text-v2:0"
    transcribe_language  = "en-US"
    cors_allowed_origins = var.domain_name != "" ? "https://${var.domain_name}" : "*"
  })
}
