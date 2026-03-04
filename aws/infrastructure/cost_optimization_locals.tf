locals {
  is_prod = var.environment == "prod"

  # In non-prod, default to cheaper idle posture unless explicitly overridden.
  enable_redis_effective                = var.enable_redis != null ? var.enable_redis : (local.is_prod || !var.cost_optimized_non_prod)
  enable_container_insights_effective   = var.enable_container_insights != null ? var.enable_container_insights : local.is_prod
  enable_vpc_interface_endpoints_effective = var.enable_vpc_interface_endpoints != null ? var.enable_vpc_interface_endpoints : (local.is_prod || !var.cost_optimized_non_prod)
  enable_ecs_idle_autoscaler_effective  = var.enable_ecs_idle_autoscaler
  enable_customer_managed_kms_effective = var.enable_customer_managed_kms != null ? var.enable_customer_managed_kms : (local.is_prod || !var.cost_optimized_non_prod)
  enable_s3_versioning_effective        = var.enable_s3_versioning != null ? var.enable_s3_versioning : (local.is_prod || !var.cost_optimized_non_prod)
}
