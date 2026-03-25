data "archive_file" "ecs_idle_scaler_zip" {
  count       = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  type        = "zip"
  output_path = "${path.module}/.terraform-artifacts/ecs-idle-scaler.zip"

  source {
    filename = "index.py"
    content  = <<-PY
import json
import os
import boto3

ecs = boto3.client("ecs")
rds = boto3.client("rds")


def _to_int(value, default):
    try:
        return int(value)
    except Exception:
        return default


def handler(event, context):
    cluster = os.environ["ECS_CLUSTER_NAME"]
    service = os.environ["ECS_SERVICE_NAME"]
    default_desired = _to_int(os.environ.get("DEFAULT_DESIRED_COUNT", "1"), 1)
    db_instance_id = os.environ.get("RDS_INSTANCE_ID", "").strip()
    allow_db_stop = os.environ.get("ALLOW_DB_STOP", "true").lower() == "true"

    desired = default_desired
    if isinstance(event, dict) and "desired_count" in event:
        desired = _to_int(event.get("desired_count"), default_desired)

    db_status = None
    db_start_requested = False
    db_stop_requested = False

    if db_instance_id:
        try:
            db_instances = rds.describe_db_instances(DBInstanceIdentifier=db_instance_id).get("DBInstances", [])
            if db_instances:
                db_status = db_instances[0].get("DBInstanceStatus")

                if desired > 0 and db_status == "stopped":
                    rds.start_db_instance(DBInstanceIdentifier=db_instance_id)
                    db_start_requested = True

                if desired == 0 and allow_db_stop and db_status == "available":
                    rds.stop_db_instance(DBInstanceIdentifier=db_instance_id)
                    db_stop_requested = True
        except Exception:
            # Keep ECS wake/sleep path resilient even if RDS calls fail.
            pass

    current = ecs.describe_services(cluster=cluster, services=[service]).get("services", [])
    current_desired = current[0].get("desiredCount") if current else None

    if current_desired == desired:
        return {
            "changed": False,
            "cluster": cluster,
            "service": service,
            "desired_count": desired,
            "db_instance_id": db_instance_id or None,
            "db_status": db_status,
            "db_start_requested": db_start_requested,
            "db_stop_requested": db_stop_requested,
            "message": "No scaling action required"
        }

    ecs.update_service(cluster=cluster, service=service, desiredCount=desired)

    return {
        "changed": True,
        "cluster": cluster,
        "service": service,
        "previous_desired_count": current_desired,
        "desired_count": desired,
        "db_instance_id": db_instance_id or None,
        "db_status": db_status,
        "db_start_requested": db_start_requested,
        "db_stop_requested": db_stop_requested
    }
PY
  }
}

resource "aws_iam_role" "ecs_idle_scaler_lambda" {
  count = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  name  = "${var.project_name}-${var.environment}-ecs-idle-scaler-role"

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
    Name = "${var.project_name}-${var.environment}-ecs-idle-scaler-role"
  }
}

resource "aws_iam_role_policy_attachment" "ecs_idle_scaler_lambda_basic" {
  count      = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  role       = aws_iam_role.ecs_idle_scaler_lambda[0].name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy" "ecs_idle_scaler_lambda" {
  count = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  name  = "${var.project_name}-${var.environment}-ecs-idle-scaler-policy"
  role  = aws_iam_role.ecs_idle_scaler_lambda[0].id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "ecs:DescribeServices",
          "ecs:UpdateService"
        ]
        Resource = [
          aws_ecs_service.api.id
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "ecs:DescribeClusters"
        ]
        Resource = [
          aws_ecs_cluster.main.arn
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "rds:DescribeDBInstances",
          "rds:StartDBInstance",
          "rds:StopDBInstance"
        ]
        Resource = [
          aws_db_instance.main.arn
        ]
      }
    ]
  })
}

resource "aws_cloudwatch_log_group" "ecs_idle_scaler_lambda" {
  count             = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  name              = "/aws/lambda/${var.project_name}-${var.environment}-ecs-idle-scaler"
  retention_in_days = var.environment == "prod" ? 30 : 1

  tags = {
    Name = "${var.project_name}-${var.environment}-ecs-idle-scaler-logs"
  }
}

resource "aws_lambda_function" "ecs_idle_scaler" {
  count            = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  function_name    = "${var.project_name}-${var.environment}-ecs-idle-scaler"
  role             = aws_iam_role.ecs_idle_scaler_lambda[0].arn
  handler          = "index.handler"
  runtime          = "python3.12"
  timeout          = 30
  filename         = data.archive_file.ecs_idle_scaler_zip[0].output_path
  source_code_hash = data.archive_file.ecs_idle_scaler_zip[0].output_base64sha256

  environment {
    variables = {
      ECS_CLUSTER_NAME      = aws_ecs_cluster.main.name
      ECS_SERVICE_NAME      = aws_ecs_service.api.name
      DEFAULT_DESIRED_COUNT = tostring(var.ecs_warm_desired_count)
      RDS_INSTANCE_ID       = aws_db_instance.main.id
      ALLOW_DB_STOP         = var.environment == "prod" ? "false" : "true"
    }
  }

  depends_on = [
    aws_cloudwatch_log_group.ecs_idle_scaler_lambda
  ]

  tags = {
    Name = "${var.project_name}-${var.environment}-ecs-idle-scaler"
  }
}

# Lambda Function URL — allows the browser WakeupGate to POST /wake directly
# (through CloudFront) and trigger an immediate ECS scale-up, bypassing the
# CloudWatch metric pipeline (~5 min delay for CF 5xxErrorRate to propagate).
resource "aws_lambda_function_url" "ecs_idle_scaler" {
  count              = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  function_name      = aws_lambda_function.ecs_idle_scaler[0].function_name
  authorization_type = "NONE"

  cors {
    allow_credentials = false
    allow_origins     = ["*"]
    allow_methods     = ["POST", "GET"]
    allow_headers     = ["content-type"]
    max_age           = 86400
  }
}

# Public invoke permissions for Lambda Function URL.
# Both actions are required so anonymous callers can invoke the URL:
# - lambda:InvokeFunctionUrl (URL endpoint permission)
# - lambda:InvokeFunction (actual function invoke, constrained to URL path)
resource "aws_lambda_permission" "allow_function_url_public" {
  count                 = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  statement_id          = "FunctionURLAllowPublicAccess"
  action                = "lambda:InvokeFunctionUrl"
  function_name         = aws_lambda_function.ecs_idle_scaler[0].function_name
  principal             = "*"
  function_url_auth_type = "NONE"
}

resource "aws_lambda_permission" "allow_function_url_invoke" {
  count                  = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  statement_id           = "FunctionURLAllowInvokeFunction"
  action                 = "lambda:InvokeFunction"
  function_name          = aws_lambda_function.ecs_idle_scaler[0].function_name
  principal              = "*"
  invoked_via_function_url = true
}

resource "aws_cloudwatch_metric_alarm" "ecs_wake_on_traffic" {
  count               = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  alarm_name          = "${var.project_name}-${var.environment}-ecs-wake-on-traffic"
  alarm_description   = "Wake ECS on any traffic: ALB request/5xx counts OR CloudFront 5xx rate. CloudFront generates its own 504 (bypassing ALB metrics) when ECS is at 0 tasks, so both signals are needed."
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 1
  threshold           = 0
  treat_missing_data  = "notBreaching"

  # m1: ALB requests forwarded to healthy targets
  metric_query {
    id          = "m1"
    return_data = false

    metric {
      namespace   = "AWS/ApplicationELB"
      metric_name = "RequestCount"
      period      = 60
      stat        = "Sum"

      dimensions = {
        LoadBalancer = aws_lb.api.arn_suffix
      }
    }
  }

  # m2: ALB-generated 5XX (503 when target group is empty)
  metric_query {
    id          = "m2"
    return_data = false

    metric {
      namespace   = "AWS/ApplicationELB"
      metric_name = "HTTPCode_ELB_5XX_Count"
      period      = 60
      stat        = "Sum"

      dimensions = {
        LoadBalancer = aws_lb.api.arn_suffix
      }
    }
  }

  # m3: CloudFront 5xx error rate (%). CloudFront generates its own 504 when it
  # cannot reach the ALB origin (connection timeout with 0 ECS tasks), so the
  # ALB never logs a request. This metric fires in that case.
  metric_query {
    id          = "m3"
    return_data = false

    metric {
      namespace   = "AWS/CloudFront"
      metric_name = "5xxErrorRate"
      period      = 60
      stat        = "Average"

      dimensions = {
        DistributionId = aws_cloudfront_distribution.frontend.id
        Region         = "Global"
      }
    }
  }

  # Fire if ALB sees any traffic (m1+m2 > 0) OR CloudFront sees any 5xx (m3 > 0).
  metric_query {
    id          = "total"
    expression  = "m1+m2+m3"
    label       = "TotalTrafficSignal"
    return_data = true
  }
}

resource "aws_cloudwatch_metric_alarm" "ecs_sleep_on_idle" {
  count               = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  alarm_name          = "${var.project_name}-${var.environment}-ecs-sleep-on-idle"
  alarm_description   = "Scale ECS API service to zero after prolonged no-traffic period"
  comparison_operator = "LessThanOrEqualToThreshold"
  evaluation_periods  = var.ecs_idle_timeout_minutes
  datapoints_to_alarm = var.ecs_idle_timeout_minutes
  threshold           = 0
  treat_missing_data  = "breaching"

  metric_query {
    id          = "req"
    return_data = false

    metric {
      namespace   = "AWS/ApplicationELB"
      metric_name = "RequestCount"
      period      = 60
      stat        = "Sum"

      dimensions = {
        LoadBalancer = aws_lb.api.arn_suffix
      }
    }
  }

  metric_query {
    id          = "req_filled"
    expression  = "FILL(req, 0)"
    label       = "RequestCountFilled"
    return_data = true
  }
}

resource "aws_cloudwatch_event_rule" "ecs_wake_on_alarm" {
  count       = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  name        = "${var.project_name}-${var.environment}-ecs-wake-on-alarm"
  description = "Invoke ECS idle scaler Lambda to wake service on traffic alarm"

  event_pattern = jsonencode({
    source      = ["aws.cloudwatch"]
    detail-type = ["CloudWatch Alarm State Change"]
    detail = {
      alarmName = [aws_cloudwatch_metric_alarm.ecs_wake_on_traffic[0].alarm_name]
      state = {
        value = ["ALARM"]
      }
    }
  })
}

resource "aws_cloudwatch_event_target" "ecs_wake_on_alarm" {
  count     = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  rule      = aws_cloudwatch_event_rule.ecs_wake_on_alarm[0].name
  target_id = "EcsIdleScalerWake"
  arn       = aws_lambda_function.ecs_idle_scaler[0].arn
  input     = jsonencode({ desired_count = var.ecs_warm_desired_count })
}

resource "aws_cloudwatch_event_rule" "ecs_sleep_on_alarm" {
  count       = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  name        = "${var.project_name}-${var.environment}-ecs-sleep-on-alarm"
  description = "Invoke ECS idle scaler Lambda to sleep service on idle alarm"

  event_pattern = jsonencode({
    source      = ["aws.cloudwatch"]
    detail-type = ["CloudWatch Alarm State Change"]
    detail = {
      alarmName = [aws_cloudwatch_metric_alarm.ecs_sleep_on_idle[0].alarm_name]
      state = {
        value = ["ALARM"]
      }
    }
  })
}

resource "aws_cloudwatch_event_target" "ecs_sleep_on_alarm" {
  count     = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  rule      = aws_cloudwatch_event_rule.ecs_sleep_on_alarm[0].name
  target_id = "EcsIdleScalerSleep"
  arn       = aws_lambda_function.ecs_idle_scaler[0].arn
  input     = jsonencode({ desired_count = 0 })
}

resource "aws_lambda_permission" "allow_events_wake" {
  count         = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  statement_id  = "AllowExecutionFromEventBridgeWake"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.ecs_idle_scaler[0].function_name
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.ecs_wake_on_alarm[0].arn
}

resource "aws_lambda_permission" "allow_events_sleep" {
  count         = local.enable_ecs_idle_autoscaler_effective ? 1 : 0
  statement_id  = "AllowExecutionFromEventBridgeSleep"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.ecs_idle_scaler[0].function_name
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.ecs_sleep_on_alarm[0].arn
}
