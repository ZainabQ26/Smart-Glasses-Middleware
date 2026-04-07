# IAM Role for Lambda
resource "aws_iam_role" "lambda_document_processor" {
  name = "${var.project_name}-${var.environment}-lambda-doc-processor-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action = "sts:AssumeRole"
      Effect = "Allow"
      Principal = {
        Service = "lambda.amazonaws.com"
      }
    }]
  })

  tags = {
    Name = "${var.project_name}-${var.environment}-lambda-doc-processor-role"
  }
}

# Lambda VPC Execution Policy
resource "aws_iam_role_policy_attachment" "lambda_vpc_execution" {
  role       = aws_iam_role.lambda_document_processor.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

# Lambda Policy for Document Processing
resource "aws_iam_role_policy" "lambda_document_processor" {
  name = "${var.project_name}-${var.environment}-lambda-doc-processor-policy"
  role = aws_iam_role.lambda_document_processor.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "s3:GetObject",
          "s3:PutObject",
          "s3:DeleteObject"
        ]
        Resource = [
          "${aws_s3_bucket.media.arn}/*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "logs:CreateLogGroup",
          "logs:CreateLogStream",
          "logs:PutLogEvents"
        ]
        Resource = "arn:aws:logs:${data.aws_region.current.name}:${data.aws_caller_identity.current.account_id}:log-group:/aws/lambda/*"
      },
      {
        Effect = "Allow"
        Action = [
          "secretsmanager:GetSecretValue"
        ]
        Resource = aws_secretsmanager_secret.db_connection.arn
      }
    ]
  })
}

# CloudWatch Log Group for Lambda
resource "aws_cloudwatch_log_group" "lambda_document_processor" {
  name              = "/aws/lambda/${var.project_name}-${var.environment}-document-processor"
  retention_in_days = var.environment == "prod" ? 30 : 1

  tags = {
    Name = "${var.project_name}-${var.environment}-lambda-doc-processor-logs"
  }

  lifecycle {
    prevent_destroy = true
  }
}

# Lambda Function
resource "aws_lambda_function" "document_processor" {
  # The zip is uploaded to S3 by CI before terraform apply to work around the
  # 50 MB direct-upload limit.  s3_bucket/s3_key support up to 250 MB unzipped.
  # A fixed key is used so redeploys overwrite the same object with no accumulation.
  s3_bucket        = var.lambda_deployment_s3_bucket
  s3_key           = var.lambda_deployment_s3_key
  source_code_hash = var.lambda_deployment_hash
  function_name = "${var.project_name}-${var.environment}-document-processor"
  role          = aws_iam_role.lambda_document_processor.arn
  handler       = "index.handler"
  runtime       = "python3.12"
  timeout       = 300 # 5 minutes
  memory_size   = 1024

  lifecycle {
    precondition {
      condition     = var.lambda_deployment_s3_bucket != "" && var.lambda_deployment_s3_key != ""
      error_message = "lambda_deployment_s3_bucket and lambda_deployment_s3_key must both be set. Upload the zip to S3 before running terraform apply."
    }
  }

  vpc_config {
    subnet_ids         = aws_subnet.private_app[*].id
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
        DB_SECRET_ARN          = aws_secretsmanager_secret.db_connection.arn
      DOCUMENTS_BUCKET      = aws_s3_bucket.media.id
      DEPLOYMENT_ID         = var.deployment_id  # Changes on every deploy
    }
  }

  tags = {
    Name = "${var.project_name}-${var.environment}-document-processor"
  }

  depends_on = [
    aws_cloudwatch_log_group.lambda_document_processor
  ]
}

# S3 Event Notification to trigger Lambda
resource "aws_s3_bucket_notification" "document_upload" {
  bucket = aws_s3_bucket.media.id

  lambda_function {
    lambda_function_arn = aws_lambda_function.document_processor.arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = "uploads/"
    filter_suffix       = ".pdf"
  }

  lambda_function {
    lambda_function_arn = aws_lambda_function.document_processor.arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = "uploads/"
    filter_suffix       = ".docx"
  }

  lambda_function {
    lambda_function_arn = aws_lambda_function.document_processor.arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = "uploads/"
    filter_suffix       = ".jpg"
  }

  lambda_function {
    lambda_function_arn = aws_lambda_function.document_processor.arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = "uploads/"
    filter_suffix       = ".jpeg"
  }

  lambda_function {
    lambda_function_arn = aws_lambda_function.document_processor.arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = "uploads/"
    filter_suffix       = ".png"
  }

  lambda_function {
    lambda_function_arn = aws_lambda_function.document_processor.arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = "uploads/"
    filter_suffix       = ".webp"
  }

  lambda_function {
    lambda_function_arn = aws_lambda_function.document_processor.arn
    events              = ["s3:ObjectCreated:*"]
    filter_prefix       = "uploads/"
    filter_suffix       = ".bmp"
  }

  depends_on = [aws_lambda_permission.allow_s3]
}

# Lambda Permission for S3 to invoke
resource "aws_lambda_permission" "allow_s3" {
  statement_id_prefix = "AllowExecutionFromS3Bucket"
  action              = "lambda:InvokeFunction"
  function_name       = aws_lambda_function.document_processor.function_name
  principal           = "s3.amazonaws.com"
  source_arn          = aws_s3_bucket.media.arn
}
