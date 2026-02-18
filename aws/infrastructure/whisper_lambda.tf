variable "whisper_image_uri" {
  description = "ECR image URI for the whisper lambda (including tag)"
  type = string
}

variable "whisper_function_name" {
  type = string
  default = "smart-glasses-whisper"
}

variable "lambda_memory_mb" {
  type = number
  default = 2048
}

variable "lambda_timeout" {
  type = number
  default = 300
}

resource "aws_iam_role" "lambda_exec" {
  name = "${var.whisper_function_name}-exec"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

/*
Note: Ensure there is a resource aws_s3_bucket.media defined in your infra. If not, replace with the correct bucket resource or add the bucket.
*/

data "aws_iam_policy_document" "lambda_assume_role" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role_policy_attachment" "lambda_basic" {
  role       = aws_iam_role.lambda_exec.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

data "aws_iam_policy_document" "s3_lambda_access" {
  statement {
    actions = ["s3:GetObject", "s3:DeleteObject", "s3:PutObject"]
    resources = ["${aws_s3_bucket.media.arn}/*"]
  }
}

resource "aws_iam_policy" "lambda_s3_access" {
  name   = "${var.whisper_function_name}-s3-access"
  policy = data.aws_iam_policy_document.s3_lambda_access.json
}

resource "aws_iam_role_policy_attachment" "lambda_s3_attach" {
  role       = aws_iam_role.lambda_exec.name
  policy_arn = aws_iam_policy.lambda_s3_access.arn
}

resource "aws_sqs_queue" "whisper_queue" {
  name = "${var.whisper_function_name}-queue"
}

data "aws_iam_policy_document" "allow_sqs_send" {
  statement {
    actions = ["sqs:SendMessage", "sqs:SendMessageBatch"]
    resources = [aws_sqs_queue.whisper_queue.arn]
  }
}

resource "aws_iam_policy" "allow_sqs_send" {
  name   = "${var.whisper_function_name}-allow-sqs-send"
  policy = data.aws_iam_policy_document.allow_sqs_send.json
}

variable "api_role_name" {
  type    = string
  default = ""
  description = "Optional: name of the IAM role (e.g. ECS or API role) to attach SQS send permission to"
}

resource "aws_iam_role_policy_attachment" "api_attach_sqs_send" {
  count      = var.api_role_name != "" ? 1 : 0
  role       = var.api_role_name
  policy_arn = aws_iam_policy.allow_sqs_send.arn
}

resource "aws_lambda_event_source_mapping" "whisper_sqs_mapping" {
  event_source_arn = aws_sqs_queue.whisper_queue.arn
  function_name    = aws_lambda_function.whisper.arn
  batch_size       = 1
}

resource "aws_lambda_function" "whisper" {
  function_name = var.whisper_function_name
  package_type  = "Image"
  image_uri     = var.whisper_image_uri
  role          = aws_iam_role.lambda_exec.arn
  memory_size   = var.lambda_memory_mb
  timeout       = var.lambda_timeout
}

output "whisper_lambda_function_name" {
  value = aws_lambda_function.whisper.function_name
}

output "whisper_sqs_queue_url" {
  value = aws_sqs_queue.whisper_queue.id
}

output "whisper_sqs_queue_arn" {
  value = aws_sqs_queue.whisper_queue.arn
}
