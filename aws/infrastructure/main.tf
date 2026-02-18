terraform {
  required_version = ">= 1.0"
  
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  # Backend configuration removed for local testing with LocalStack.
  # Configure a remote S3 backend when deploying to real AWS.
}

provider "aws" {
  region = var.aws_region
  default_tags {
    tags = {
      Project     = "SmartGlassesMiddleware"
      Environment = var.environment
      ManagedBy   = "Terraform"
    }
  }

  # Optional LocalStack endpoints block (used when `use_localstack = true`).
  endpoints {
    apigateway     = var.use_localstack ? var.localstack_endpoint : ""
    cloudformation = var.use_localstack ? var.localstack_endpoint : ""
    cloudwatch     = var.use_localstack ? var.localstack_endpoint : ""
    dynamodb       = var.use_localstack ? var.localstack_endpoint : ""
    ec2            = var.use_localstack ? var.localstack_endpoint : ""
    ecr            = var.use_localstack ? var.localstack_endpoint : ""
    ecs            = var.use_localstack ? var.localstack_endpoint : ""
    elb            = var.use_localstack ? var.localstack_endpoint : ""
    iam            = var.use_localstack ? var.localstack_endpoint : ""
    lambda         = var.use_localstack ? var.localstack_endpoint : ""
    rds            = var.use_localstack ? var.localstack_endpoint : ""
    s3             = var.use_localstack ? var.localstack_endpoint : ""
    secretsmanager = var.use_localstack ? var.localstack_endpoint : ""
    sqs            = var.use_localstack ? var.localstack_endpoint : ""
    sts            = var.use_localstack ? var.localstack_endpoint : ""
  }
}
