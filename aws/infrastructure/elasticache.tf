# ElastiCache Subnet Group
resource "aws_elasticache_subnet_group" "main" {
  name       = "${var.project_name}-${var.environment}-elasticache-subnet-group"
  subnet_ids = aws_subnet.private_app[*].id

  tags = {
    Name = "${var.project_name}-${var.environment}-elasticache-subnet-group"
  }
}

# ElastiCache Parameter Group for Redis 7
resource "aws_elasticache_parameter_group" "redis" {
  name   = "${var.project_name}-${var.environment}-redis-params"
  family = "redis7"

  parameter {
    name  = "maxmemory-policy"
    value = "allkeys-lru"
  }

  parameter {
    name  = "timeout"
    value = "300"
  }

  tags = {
    Name = "${var.project_name}-${var.environment}-redis-params"
  }
}

# ElastiCache Replication Group (Redis Cluster)
resource "aws_elasticache_replication_group" "redis" {
  replication_group_id       = "${var.project_name}-${var.environment}-redis"
  description                = "Redis cluster for Smart Glasses Middleware"
  engine                     = "redis"
  engine_version             = "7.1"
  node_type                  = var.elasticache_node_type
  num_cache_clusters         = 2 # Primary + 1 replica
  parameter_group_name       = aws_elasticache_parameter_group.redis.name
  port                       = 6379
  subnet_group_name          = aws_elasticache_subnet_group.main.name
  security_group_ids         = [aws_security_group.elasticache.id]

  # Automatic failover for high availability
  automatic_failover_enabled = true
  multi_az_enabled          = true

  # Backup configuration
  snapshot_retention_limit = var.environment == "prod" ? 7 : 1
  snapshot_window          = "03:00-05:00"
  maintenance_window       = "mon:05:00-mon:06:00"

  # Encryption
  at_rest_encryption_enabled = true
  transit_encryption_enabled = true
  auth_token                 = random_password.redis_auth_token.result

  # Notifications
  notification_topic_arn = aws_sns_topic.elasticache_notifications.arn

  # Log delivery
  log_delivery_configuration {
    destination      = aws_cloudwatch_log_group.elasticache_slow_log.name
    destination_type = "cloudwatch-logs"
    log_format       = "json"
    log_type         = "slow-log"
  }

  log_delivery_configuration {
    destination      = aws_cloudwatch_log_group.elasticache_engine_log.name
    destination_type = "cloudwatch-logs"
    log_format       = "json"
    log_type         = "engine-log"
  }

  tags = {
    Name = "${var.project_name}-${var.environment}-redis"
  }

  lifecycle {
    ignore_changes = [auth_token]
  }
}

# Note: For single-node deployment (dev/test), use aws_elasticache_cluster instead
resource "aws_elasticache_cluster" "redis_single" {
  count = var.environment == "dev" ? 1 : 0

  cluster_id           = "${var.project_name}-${var.environment}-redis-single"
  engine               = "redis"
  engine_version       = "7.1"
  node_type            = var.elasticache_node_type
  num_cache_nodes      = 1
  parameter_group_name = aws_elasticache_parameter_group.redis.name
  port                 = 6379
  subnet_group_name    = aws_elasticache_subnet_group.main.name
  security_group_ids   = [aws_security_group.elasticache.id]

  snapshot_retention_limit = 1
  snapshot_window          = "03:00-05:00"

  tags = {
    Name = "${var.project_name}-${var.environment}-redis-single"
  }
}

# Generate random auth token for Redis
resource "random_password" "redis_auth_token" {
  length  = 32
  special = false # Redis auth token doesn't support special characters
}

# Store Redis credentials in Secrets Manager
resource "aws_secretsmanager_secret" "redis_credentials" {
  name        = "${var.project_name}-${var.environment}-redis-credentials"
  description = "ElastiCache Redis credentials"

  tags = {
    Name = "${var.project_name}-${var.environment}-redis-credentials"
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_secretsmanager_secret_version" "redis_credentials" {
  secret_id = aws_secretsmanager_secret.redis_credentials.id
  secret_string = jsonencode({
    auth_token        = random_password.redis_auth_token.result
    endpoint          = var.environment == "prod" ? aws_elasticache_replication_group.redis.primary_endpoint_address : (var.environment == "dev" ? aws_elasticache_cluster.redis_single[0].cache_nodes[0].address : aws_elasticache_replication_group.redis.primary_endpoint_address)
    port              = 6379
    connection_string = var.environment == "prod" ? "${aws_elasticache_replication_group.redis.primary_endpoint_address}:6379,password=${random_password.redis_auth_token.result}" : (var.environment == "dev" ? "${aws_elasticache_cluster.redis_single[0].cache_nodes[0].address}:6379" : "${aws_elasticache_replication_group.redis.primary_endpoint_address}:6379,password=${random_password.redis_auth_token.result}")
  })

  depends_on = [
    aws_elasticache_replication_group.redis,
    aws_elasticache_cluster.redis_single
  ]

  lifecycle {
    ignore_changes = [secret_string]
  }
}

# CloudWatch Log Groups for ElastiCache
resource "aws_cloudwatch_log_group" "elasticache_slow_log" {
  name              = "/aws/elasticache/${var.project_name}-${var.environment}/slow-log"
  retention_in_days = var.environment == "prod" ? 30 : 7

  tags = {
    Name = "${var.project_name}-${var.environment}-elasticache-slow-log"
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_cloudwatch_log_group" "elasticache_engine_log" {
  name              = "/aws/elasticache/${var.project_name}-${var.environment}/engine-log"
  retention_in_days = var.environment == "prod" ? 30 : 7

  tags = {
    Name = "${var.project_name}-${var.environment}-elasticache-engine-log"
  }

  lifecycle {
    prevent_destroy = true
  }
}

# SNS Topic for ElastiCache notifications
resource "aws_sns_topic" "elasticache_notifications" {
  name = "${var.project_name}-${var.environment}-elasticache-notifications"

  tags = {
    Name = "${var.project_name}-${var.environment}-elasticache-notifications"
  }
}
# Updated 02/18/2026 17:52:17
