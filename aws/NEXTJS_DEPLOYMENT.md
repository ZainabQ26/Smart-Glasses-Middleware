# Deploying Next.js + .NET to AWS ECS

This guide shows how to deploy both the Next.js frontend and .NET backend to AWS ECS/Fargate with ALB routing.

## Architecture

```
Internet → ALB → Target Groups
              ├─ Default (/) → Next.js (port 3000)
              ├─ /api/* → .NET API (port 5000)
              └─ /hub/* → .NET API (port 5000)
```

## Prerequisites

1. ECR repositories created:
   - `smart-glasses-api`
   - `smart-glasses-frontend`

2. Base infrastructure deployed:
   - VPC, subnets, security groups
   - RDS (PostgreSQL)
   - ElastiCache (Redis)
   - S3 bucket for media

## Step 1: Build and Push Images

### Backend Image

```bash
cd src/SmartGlassesMiddleware

# Build
docker build -t smart-glasses-api:latest .

# Tag and push to ECR
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin <account-id>.dkr.ecr.us-east-1.amazonaws.com

docker tag smart-glasses-api:latest <account-id>.dkr.ecr.us-east-1.amazonaws.com/smart-glasses-api:latest
docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/smart-glasses-api:latest
```

### Frontend Image

```bash
cd next-app

# Build
docker build -t smart-glasses-frontend:latest .

# Tag and push to ECR
docker tag smart-glasses-frontend:latest <account-id>.dkr.ecr.us-east-1.amazonaws.com/smart-glasses-frontend:latest
docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/smart-glasses-frontend:latest
```

## Step 2: Deploy with Terraform

Add the following to your Terraform configuration:

```hcl
# ECS Task for .NET Backend API
resource "aws_ecs_task_definition" "api" {
  family                   = "smart-glasses-api"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "1024"
  memory                   = "2048"
  execution_role_arn       = aws_iam_role.ecs_execution_role.arn
  task_role_arn            = aws_iam_role.ecs_task_role.arn

  container_definitions = jsonencode([{
    name  = "api"
    image = "${var.ecr_repository_url}/smart-glasses-api:latest"
    
    portMappings = [{
      containerPort = 5000
      protocol      = "tcp"
    }]
    
    environment = [
      { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
      { name = "ASPNETCORE_URLS", value = "http://+:5000" },
      { name = "MIGRATE_ON_STARTUP", value = "true" }
    ]
    
    secrets = [
      { name = "ConnectionStrings__DefaultConnection", valueFrom = aws_secretsmanager_secret.db_connection.arn },
      { name = "Redis__Configuration", valueFrom = aws_secretsmanager_secret.redis_connection.arn }
    ]
    
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.api.name
        "awslogs-region"        = var.aws_region
        "awslogs-stream-prefix" = "api"
      }
    }
  }])
}

# ECS Task for Next.js Frontend
resource "aws_ecs_task_definition" "frontend" {
  family                   = "smart-glasses-frontend"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = "512"
  memory                   = "1024"
  execution_role_arn       = aws_iam_role.ecs_execution_role.arn

  container_definitions = jsonencode([{
    name  = "frontend"
    image = "${var.ecr_repository_url}/smart-glasses-frontend:latest"
    
    portMappings = [{
      containerPort = 3000
      protocol      = "tcp"
    }]
    
    environment = [
      { name = "NODE_ENV", value = "production" },
      { name = "BACKEND_URL", value = "http://api.local:5000" } # Internal ALB or service discovery
    ]
    
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.frontend.name
        "awslogs-region"        = var.aws_region
        "awslogs-stream-prefix" = "frontend"
      }
    }
  }])
}

# ECS Services
resource "aws_ecs_service" "api" {
  name            = "smart-glasses-api"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 2
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [aws_security_group.ecs_tasks.id]
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 5000
  }
}

resource "aws_ecs_service" "frontend" {
  name            = "smart-glasses-frontend"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.frontend.arn
  desired_count   = 2
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [aws_security_group.ecs_tasks.id]
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.frontend.arn
    container_name   = "frontend"
    container_port   = 3000
  }
}

# ALB Target Groups
resource "aws_lb_target_group" "api" {
  name        = "smart-glasses-api"
  port        = 5000
  protocol    = "HTTP"
  vpc_id      = var.vpc_id
  target_type = "ip"

  health_check {
    path                = "/health"
    interval            = 30
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }
}

resource "aws_lb_target_group" "frontend" {
  name        = "smart-glasses-frontend"
  port        = 3000
  protocol    = "HTTP"
  vpc_id      = var.vpc_id
  target_type = "ip"

  health_check {
    path                = "/health"
    interval            = 30
    timeout             = 5
    healthy_threshold   = 2
    unhealthy_threshold = 3
  }
}

# ALB Listener Rules
resource "aws_lb_listener_rule" "api" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 100

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }

  condition {
    path_pattern {
      values = ["/api/*", "/hub/*", "/health", "/test"]
    }
  }
}

resource "aws_lb_listener_rule" "frontend" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 200

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.frontend.arn
  }

  condition {
    path_pattern {
      values = ["/*"]
    }
  }
}
```

## Step 3: Configure Internal Service Communication

For the Next.js frontend to communicate with the .NET backend internally (not through the public ALB), you have two options:

### Option A: AWS Cloud Map Service Discovery

```hcl
resource "aws_service_discovery_private_dns_namespace" "main" {
  name = "smartglasses.local"
  vpc  = var.vpc_id
}

resource "aws_service_discovery_service" "api" {
  name = "api"

  dns_config {
    namespace_id = aws_service_discovery_private_dns_namespace.main.id
    
    dns_records {
      ttl  = 10
      type = "A"
    }
  }
}

# Add to ECS service:
resource "aws_ecs_service" "api" {
  # ... existing config ...
  
  service_registries {
    registry_arn = aws_service_discovery_service.api.arn
  }
}
```

Then set `BACKEND_URL=http://api.smartglasses.local:5000` in frontend task definition.

### Option B: Internal ALB

Create an internal ALB for backend services and use its DNS name in `BACKEND_URL`.

## Step 4: Deploy

```bash
cd aws/infrastructure
terraform init
terraform plan
terraform apply
```

## Cost Optimization

- Use Fargate Spot for non-critical workloads
- Enable ECS Exec for debugging instead of SSH
- Use CloudWatch Container Insights for monitoring
- Consider AWS App Runner for simpler deployments (single container)

## Monitoring

- CloudWatch Logs: `/ecs/smart-glasses-api` and `/ecs/smart-glasses-frontend`
- CloudWatch Alarms: API 5xx errors, high memory/CPU, unhealthy targets
- X-Ray: Enable tracing in both services for request flow analysis

## Troubleshooting

### Frontend can't reach backend

Check:
1. Security groups allow traffic between frontend and backend tasks
2. `BACKEND_URL` is set correctly (internal URL, not public)
3. Backend service is healthy (`/health` endpoint)

### SignalR WebSocket connection fails

Ensure:
1. ALB listener rules forward `/hub/*` to backend target group
2. Sticky sessions enabled on backend target group
3. Security groups allow WebSocket traffic (HTTP upgrade)
