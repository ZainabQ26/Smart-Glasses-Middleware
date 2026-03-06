# ECR Repository for API
resource "aws_ecr_repository" "api" {
  name                 = "${var.project_name}-${var.environment}-api"
  image_tag_mutability = "MUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }

  encryption_configuration {
    encryption_type = local.enable_customer_managed_kms_effective ? "KMS" : "AES256"
    kms_key         = local.enable_customer_managed_kms_effective ? aws_kms_key.ecr[0].arn : null
  }

  tags = {
    Name = "${var.project_name}-${var.environment}-api"
  }
}

# ECR Lifecycle Policy
resource "aws_ecr_lifecycle_policy" "api" {
  repository = aws_ecr_repository.api.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Keep last 5 tagged images"
        selection = {
          tagStatus     = "tagged"
          tagPatternList = ["*"]
          countType     = "imageCountMoreThan"
          countNumber   = 5
        }
        action = {
          type = "expire"
        }
      },
      {
        rulePriority = 2
        description  = "Remove untagged images after 1 day"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 1
        }
        action = {
          type = "expire"
        }
      }
    ]
  })
}

# KMS Key for ECR encryption
resource "aws_kms_key" "ecr" {
  count = local.enable_customer_managed_kms_effective ? 1 : 0
  description             = "KMS key for ECR encryption"
  deletion_window_in_days = var.environment == "prod" ? 30 : 7
  enable_key_rotation     = true

  tags = {
    Name = "${var.project_name}-${var.environment}-ecr-kms"
  }
}

resource "aws_kms_alias" "ecr" {
  count         = local.enable_customer_managed_kms_effective ? 1 : 0
  name          = "alias/${var.project_name}-${var.environment}-ecr"
  target_key_id = aws_kms_key.ecr[0].key_id
}
