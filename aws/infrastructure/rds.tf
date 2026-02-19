locals {
  rds_endpoint = aws_db_instance.main.address
}

# DB Subnet Group
resource "aws_db_subnet_group" "main" {
  name       = "${var.project_name}-${var.environment}-db-subnet-group"
  subnet_ids = aws_subnet.private_db[*].id

  tags = {
    Name = "${var.project_name}-${var.environment}-db-subnet-group"
  }

  lifecycle {
    prevent_destroy = true
    # Cannot move subnet group between VPCs (InvalidParameterValue: new subnets
    # not in same VPC as existing group). The existing group has correct subnets.
    ignore_changes = [subnet_ids]
  }
}

# DB Parameter Group for standard PostgreSQL 16
resource "aws_db_parameter_group" "main" {
  name   = "${var.project_name}-${var.environment}-pg16-params"
  family = "postgres16"
  description = "PostgreSQL 16 parameter group"

  parameter {
    name  = "log_statement"
    value = "all"
  }

  parameter {
    name  = "log_min_duration_statement"
    value = "1000"
  }

  tags = {
    Name = "${var.project_name}-${var.environment}-pg16-params"
  }

  lifecycle {
    prevent_destroy = true
  }
}

# Generate random password for RDS
resource "random_password" "db_password" {
  length  = 32
  special = true
}

# Store DB credentials in Secrets Manager
resource "aws_secretsmanager_secret" "db_credentials" {
  name        = "${var.project_name}-${var.environment}-db-credentials"
  description = "RDS Aurora PostgreSQL credentials"

  tags = {
    Name = "${var.project_name}-${var.environment}-db-credentials"
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_secretsmanager_secret_version" "db_credentials" {
  secret_id = aws_secretsmanager_secret.db_credentials.id
  secret_string = jsonencode({
    username          = var.db_username
    password          = random_password.db_password.result
    engine            = "postgres"
    host              = local.rds_endpoint
    port              = 5432
    dbname            = var.db_name
    connection_string = "Host=${local.rds_endpoint};Port=5432;Database=${var.db_name};Username=${var.db_username};Password=${random_password.db_password.result}"
  })

  lifecycle {
    ignore_changes = [secret_string]
  }
}

# RDS PostgreSQL (free-tier eligible: db.t3.micro, single-AZ, gp2 20 GB)
resource "aws_db_instance" "main" {
  identifier     = "${var.project_name}-${var.environment}-postgres"
  engine         = "postgres"
  engine_version = "16"
  instance_class = var.db_instance_class

  allocated_storage     = 20
  max_allocated_storage = 100
  storage_type          = "gp2"
  storage_encrypted     = true
  kms_key_id            = aws_kms_key.rds.arn

  db_name  = var.db_name
  username = var.db_username
  password = random_password.db_password.result

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.rds.id]
  parameter_group_name   = aws_db_parameter_group.main.name

  # Single-AZ (free tier); set to true for production HA
  multi_az = false

  backup_retention_period      = var.environment == "prod" ? 30 : 1
  preferred_backup_window      = "03:00-04:00"
  preferred_maintenance_window = "mon:04:00-mon:05:00"

  # Performance Insights (available on db.t3.micro, 7-day free retention)
  performance_insights_enabled = true

  # Enhanced Monitoring
  monitoring_interval = 60
  monitoring_role_arn = aws_iam_role.rds_monitoring.arn

  deletion_protection = var.environment == "prod" ? true : false
  skip_final_snapshot = var.environment != "prod"
  final_snapshot_identifier = var.environment == "prod" ? "${var.project_name}-${var.environment}-final-snapshot" : null

  enabled_cloudwatch_logs_exports = ["postgresql", "upgrade"]

  tags = {
    Name = "${var.project_name}-${var.environment}-postgres"
  }

  lifecycle {
    ignore_changes  = [password, final_snapshot_identifier]
    prevent_destroy = true
  }
}

# KMS Key for RDS encryption
resource "aws_kms_key" "rds" {
  description             = "KMS key for RDS encryption"
  deletion_window_in_days = var.environment == "prod" ? 30 : 7
  enable_key_rotation     = true

  tags = {
    Name = "${var.project_name}-${var.environment}-rds-kms"
  }

  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_kms_alias" "rds" {
  name          = "alias/${var.project_name}-${var.environment}-rds"
  target_key_id = aws_kms_key.rds.key_id
}

# IAM Role for RDS Enhanced Monitoring
resource "aws_iam_role" "rds_monitoring" {
  name = "${var.project_name}-${var.environment}-rds-monitoring-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Action = "sts:AssumeRole"
      Effect = "Allow"
      Principal = {
        Service = "monitoring.rds.amazonaws.com"
      }
    }]
  })

  tags = {
    Name = "${var.project_name}-${var.environment}-rds-monitoring-role"
  }
}

resource "aws_iam_role_policy_attachment" "rds_monitoring" {
  role       = aws_iam_role.rds_monitoring.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonRDSEnhancedMonitoringRole"
}
