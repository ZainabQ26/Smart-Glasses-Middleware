terraform {
  required_version = ">= 1.0"
  
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  backend "s3" {
    # Configure this in terraform init
    # bucket = "your-terraform-state-bucket"
    # key    = "smart-glasses/terraform.tfstate"
    # region = "us-east-1"
  }
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
}
