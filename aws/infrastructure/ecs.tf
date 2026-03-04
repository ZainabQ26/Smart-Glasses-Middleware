# ECS Cluster
resource "aws_ecs_cluster" "main" {
  name = "${var.project_name}-${var.environment}-cluster"

  setting {
    name  = "containerInsights"
    value = local.enable_container_insights_effective ? "enabled" : "disabled"
  }

  tags = {
    Name = "${var.project_name}-${var.environment}-cluster"
  }

  lifecycle {
    prevent_destroy = true
  }
}

# ECS Cluster Capacity Providers
resource "aws_ecs_cluster_capacity_providers" "main" {
  cluster_name = aws_ecs_cluster.main.name

  capacity_providers = ["FARGATE", "FARGATE_SPOT"]

  default_capacity_provider_strategy {
    capacity_provider = "FARGATE"
    weight            = 1
    base              = 1
  }
}

# CloudWatch Log Group for ECS
resource "aws_cloudwatch_log_group" "ecs_api" {
  name              = "/ecs/${var.project_name}-${var.environment}/api"
  retention_in_days = var.environment == "prod" ? 30 : 1

  tags = {
    Name = "${var.project_name}-${var.environment}-ecs-api-logs"
  }

  lifecycle {
    prevent_destroy = true
  }
}

# IAM Role for ECS Task Execution
resource "aws_iam_role" "ecs_task_execution" {
  name = "${var.project_name}-${var.environment}-ecs-task-execution-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action = "sts:AssumeRole"
      Effect = "Allow"
      Principal = {
        Service = "ecs-tasks.amazonaws.com"
      }
    }]
  })

}

resource "aws_iam_role_policy_attachment" "ecs_task_execution" {
  role       = aws_iam_role.ecs_task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# Additional policy for Secrets Manager and ECR
resource "aws_iam_role_policy" "ecs_task_execution_secrets" {
  name = "${var.project_name}-${var.environment}-ecs-secrets-policy"
  role = aws_iam_role.ecs_task_execution.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat([
      {
        Effect = "Allow"
        Action = [
          "secretsmanager:GetSecretValue"
        ]
        Resource = concat([
          aws_secretsmanager_secret.app_secrets.arn,
          aws_secretsmanager_secret.db_credentials.arn
        ], local.enable_customer_managed_kms_effective ? [aws_kms_key.s3[0].arn] : [], local.enable_redis_effective ? [aws_secretsmanager_secret.redis_credentials[0].arn] : [])
        
      }
    ], local.enable_customer_managed_kms_effective ? [
      {
        Effect = "Allow"
        Action = [
          "kms:Decrypt"
        ]
        Resource = [
          aws_kms_key.s3[0].arn
        ]
      }
    ] : [])
  })
}

# IAM Role for ECS Task
resource "aws_iam_role" "ecs_task" {
  name = "${var.project_name}-${var.environment}-ecs-task-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action = "sts:AssumeRole"
      Effect = "Allow"
      Principal = {
        Service = "ecs-tasks.amazonaws.com"
      }
    }]
  })

  tags = {
    Name = "${var.project_name}-${var.environment}-ecs-task-role"
  }
}

# Policy for ECS Task to access AWS services
resource "aws_iam_role_policy" "ecs_task" {
  name = "${var.project_name}-${var.environment}-ecs-task-policy"
  role = aws_iam_role.ecs_task.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat([
      {
        Effect = "Allow"
        Action = [
          "s3:GetObject",
          "s3:PutObject",
          "s3:DeleteObject",
          "s3:ListBucket"
        ]
        Resource = [
          aws_s3_bucket.media.arn,
          "${aws_s3_bucket.media.arn}/*",
          aws_s3_bucket.documents.arn,
          "${aws_s3_bucket.documents.arn}/*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "bedrock:InvokeModel",
          "bedrock:InvokeModelWithResponseStream",
          "bedrock:Converse",
          "bedrock:ConverseStream"
        ]
        Resource = [
          "arn:aws:bedrock:*::foundation-model/*",
          "arn:aws:bedrock:*:*:inference-profile/*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "transcribe:StartTranscriptionJob",
          "transcribe:GetTranscriptionJob",
          "transcribe:DeleteTranscriptionJob"
        ]
        Resource = "*"
      },
      {
        Effect = "Allow"
        Action = [
          "lambda:InvokeFunction"
        ]
        Resource = [
          aws_lambda_function.document_processor.arn,
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "logs:CreateLogGroup",
          "logs:CreateLogStream",
          "logs:PutLogEvents"
        ]
        Resource = "*"
      },
      {
        Effect = "Allow"
        Action = [
          "secretsmanager:GetSecretValue"
        ]
        Resource = concat([
          aws_secretsmanager_secret.app_secrets.arn,
          aws_secretsmanager_secret.db_credentials.arn
        ], local.enable_redis_effective ? [aws_secretsmanager_secret.redis_credentials[0].arn] : [])
      }
    ], local.enable_customer_managed_kms_effective ? [
      {
        # KMS permissions required for S3 SSE-KMS (PutObject needs GenerateDataKey, GetObject needs Decrypt)
        Effect = "Allow"
        Action = [
          "kms:GenerateDataKey",
          "kms:Decrypt",
          "kms:DescribeKey"
        ]
        Resource = aws_kms_key.s3[0].arn
      }
    ] : [])
  })
}

# ECS Task Definition for API
resource "aws_ecs_task_definition" "api" {
  family                   = "${var.project_name}-${var.environment}-api"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = var.ecs_api_cpu
  memory                   = var.ecs_api_memory
  execution_role_arn       = aws_iam_role.ecs_task_execution.arn
  task_role_arn            = aws_iam_role.ecs_task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  # Use provided API image if set (e.g., from CI), otherwise fall back to ECR repo :latest
  container_definitions = local.api_container_definitions

  tags = {
    Name = "${var.project_name}-${var.environment}-api-task"
  }
}

locals {
  use_public_network_for_ecs = var.dev_public_network_mode && var.environment != "prod"
  ecs_service_subnet_ids     = local.use_public_network_for_ecs ? aws_subnet.public[*].id : aws_subnet.private_app[*].id
  ecs_assign_public_ip       = local.use_public_network_for_ecs

  api_image_to_use = var.api_image != "" ? var.api_image : "${aws_ecr_repository.api.repository_url}:latest"

  api_container_definitions = jsonencode([
    {
      name      = "api"
      image     = local.api_image_to_use
      essential = true

      portMappings = [{
        containerPort = 5000
        protocol      = "tcp"
      }]

      environment = [
        {
          name  = "ASPNETCORE_ENVIRONMENT"
          value = var.environment == "prod" ? "Production" : "AWS"
        },
        {
          name  = "AWS_REGION"
          value = var.aws_region
        },
        {
          name  = "DEPLOYMENT_ID"
          value = var.deployment_id
        },
        {
          # Migrations are applied by the CI run-migrations job (one-shot Fargate task
          # with MIGRATE_ONLY=true) before new tasks handle traffic. No migration on
          # every cold start.
          name  = "MIGRATE_ON_STARTUP"
          value = "false"
        },
      ]

      secrets = concat(
        [
          {
            name      = "ConnectionStrings__DefaultConnection"
            valueFrom = "${aws_secretsmanager_secret.db_credentials.arn}:connection_string::"
          },
          {
            name      = "AWS__S3__MediaBucket"
            valueFrom = "${aws_secretsmanager_secret.app_secrets.arn}:s3_media_bucket::"
          },
          {
            name      = "AWS__S3__DocumentsBucket"
            valueFrom = "${aws_secretsmanager_secret.app_secrets.arn}:s3_documents_bucket::"
          }
        ],
        local.enable_redis_effective ? [{
          name      = "ConnectionStrings__Redis"
          valueFrom = "${aws_secretsmanager_secret.redis_credentials[0].arn}:connection_string::"
        }] : []
      )

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.ecs_api.name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = "ecs"
        }
      }

      healthCheck = {
        command     = ["CMD-SHELL", "curl -f http://localhost:5000/health || exit 1"]
        interval    = 10  # reduced from 30 s; matches ALB interval
        timeout     = 5
        retries     = 3
        startPeriod = 20  # reduced from 60 s; .NET 10 + ReadyToRun starts in ~15-20 s
      }
    }
  ])

}

# ECS Service for API
resource "aws_ecs_service" "api" {
  name            = "${var.project_name}-${var.environment}-api-service"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = var.ecs_api_desired_count
  launch_type     = "FARGATE"
  force_new_deployment = true  # Force new deployment on every terraform apply

  network_configuration {
    subnets          = local.ecs_service_subnet_ids
    security_groups  = [aws_security_group.ecs_tasks.id]
    assign_public_ip = local.ecs_assign_public_ip
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 5000
  }

  health_check_grace_period_seconds = 30  # reduced from 120 s; ECS container is healthy by ~20-25 s with new intervals

  deployment_maximum_percent         = 200
  deployment_minimum_healthy_percent = 100
  
  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  enable_execute_command = true

  tags = {
    Name = "${var.project_name}-${var.environment}-api-service"
  }

  lifecycle {
    # desired_count is managed by Application Auto Scaling at runtime;
    # ignore it here so Terraform doesn't reset it on every apply.
    ignore_changes = [desired_count]
  }

  depends_on = [
    aws_lb_listener.api_http
  ]
}

# Auto Scaling for ECS Service
resource "aws_appautoscaling_target" "ecs_api" {
  max_capacity       = var.ecs_api_max_capacity
  min_capacity       = var.ecs_api_min_capacity
  resource_id        = "service/${aws_ecs_cluster.main.name}/${aws_ecs_service.api.name}"
  scalable_dimension = "ecs:service:DesiredCount"
  service_namespace  = "ecs"
}

resource "aws_appautoscaling_policy" "ecs_api_cpu" {
  name               = "${var.project_name}-${var.environment}-api-cpu-scaling"
  policy_type        = "TargetTrackingScaling"
  resource_id        = aws_appautoscaling_target.ecs_api.resource_id
  scalable_dimension = aws_appautoscaling_target.ecs_api.scalable_dimension
  service_namespace  = aws_appautoscaling_target.ecs_api.service_namespace

  target_tracking_scaling_policy_configuration {
    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }
    target_value       = 70.0
    scale_in_cooldown  = 300
    scale_out_cooldown = 60
  }
}

resource "aws_appautoscaling_policy" "ecs_api_memory" {
  name               = "${var.project_name}-${var.environment}-api-memory-scaling"
  policy_type        = "TargetTrackingScaling"
  resource_id        = aws_appautoscaling_target.ecs_api.resource_id
  scalable_dimension = aws_appautoscaling_target.ecs_api.scalable_dimension
  service_namespace  = aws_appautoscaling_target.ecs_api.service_namespace

  target_tracking_scaling_policy_configuration {
    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageMemoryUtilization"
    }
    target_value       = 80.0
    scale_in_cooldown  = 300
    scale_out_cooldown = 60
  }
}
