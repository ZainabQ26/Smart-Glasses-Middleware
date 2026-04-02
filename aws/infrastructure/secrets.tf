
resource "aws_secretsmanager_secret" "db_connection" {
  name                    = "${local.app_name}-db-connection-${local.environment}"
  recovery_window_in_days = 0  # Allow immediate deletion in dev

  tags = {
    environment = local.environment
    app_name    = local.app_name
  }
}

resource "aws_secretsmanager_secret_version" "db_connection" {
  secret_id = aws_secretsmanager_secret.db_connection.id
  secret_string = jsonencode({
    username = var.db_username
    password = var.db_password
    host     = var.db_host
    port     = var.db_port
    dbname   = var.db_name
  })
}
# Application Secrets
resource "aws_secretsmanager_secret" "app_secrets" {
  count       = var.environment == "prod" ? 1 : 0
  name        = "${var.project_name}-${var.environment}-app-secrets"
  description = "Application secrets for Smart Glasses Middleware"

  tags = {
    Name = "${var.project_name}-${var.environment}-app-secrets"
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_secretsmanager_secret_version" "app_secrets" {
  count     = var.environment == "prod" ? 1 : 0
  secret_id = aws_secretsmanager_secret.app_secrets[0].id
  secret_string = jsonencode({
    s3_media_bucket      = aws_s3_bucket.media.id
    s3_documents_bucket  = aws_s3_bucket.documents.id
    bedrock_model_id     = "anthropic.claude-3-sonnet-20240229-v1:0"
    embedding_model_id   = "amazon.titan-embed-text-v2:0"
    transcribe_language  = "en-US"
    cors_allowed_origins = var.domain_name != "" ? "https://${var.domain_name}" : "*"
  })

  lifecycle {
    ignore_changes = [
      secret_string
    ]
  }
}
