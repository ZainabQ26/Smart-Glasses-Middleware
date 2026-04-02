terraform {
  required_version = ">= 1.0"
  
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    archive = {
      source  = "hashicorp/archive"
      version = "~> 2.5"
    }
  }

  # S3 backend – populated at runtime via `terraform init -backend-config` flags.
  # Leave the block empty here so that local runs (LocalStack / -backend=false)
  # can override it without changing this file.
  backend "s3" {}
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
  # null means "use the real AWS endpoint"; the AWS provider rejects empty strings.
  dynamic "endpoints" {
    for_each = var.use_localstack ? [1] : []
    content {
      apigateway     = var.localstack_endpoint
      cloudformation = var.localstack_endpoint
      cloudwatch     = var.localstack_endpoint
      dynamodb       = var.localstack_endpoint
      ec2            = var.localstack_endpoint
      ecr            = var.localstack_endpoint
      ecs            = var.localstack_endpoint
      elb            = var.localstack_endpoint
      iam            = var.localstack_endpoint
      lambda         = var.localstack_endpoint
      rds            = var.localstack_endpoint
      s3             = var.localstack_endpoint
      secretsmanager = var.localstack_endpoint
      sqs            = var.localstack_endpoint
      sts            = var.localstack_endpoint
    }
  }

  dynamic "endpoints" {
    for_each = var.use_localstack ? [1] : []
    content {
      ec2                = "http://localhost:4566"
      s3                 = "http://localhost:4566"
      iam                = "http://localhost:4566"
      rds                = "http://localhost:4566"
      lambda             = "http://localhost:4566"
      apigateway         = "http://localhost:4566"
      cloudformation     = "http://localhost:4566"
      ecs                = "http://localhost:4566"
      ecr                = "http://localhost:4566"
      logs               = "http://localhost:4566"
      secretsmanager     = "http://localhost:4566"
      dynamodb           = "http://localhost:4566"
      kms                = "http://localhost:4566"
      cloudwatch         = "http://localhost:4566"
      sns                = "http://localhost:4566"
      sqs                = "http://localhost:4566"
      sts                = "http://localhost:4566"
      kinesis            = "http://localhost:4566"
      bedrock            = "http://localhost:4566"
      bedrock_runtime    = "http://localhost:4566"
      route53            = "http://localhost:4566"
      cloudfront         = "http://localhost:4566"
      acm                = "http://localhost:4566"
      elasticloadbalancing = "http://localhost:4566"
    }
    }
  }
# Data sources for current AWS account and region
data "aws_region" "current" {
}

data "aws_caller_identity" "current" {
}
