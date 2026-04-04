# Response headers policy that enables cross-origin isolation.
# COOP + COEP together enable SharedArrayBuffer in the browser, which allows
# onnxruntime-web to use its threaded SIMD WASM build for faster inference.
# @huggingface/transformers v3 (replacing @xenova/transformers v2) bundles
# onnxruntime-web 1.21+ which fixes the "Unknown CPU vendor" crash that
# previously forced us to remove COEP.
resource "aws_cloudfront_response_headers_policy" "coop_coep" {
  name    = "${var.project_name}-${var.environment}-coop-coep"
  comment = "COOP + COEP for SharedArrayBuffer / ONNX Runtime threaded WASM"

  custom_headers_config {
    items {
      header   = "Cross-Origin-Opener-Policy"
      value    = "same-origin"
      override = true
    }
    items {
      # credentialless: cross-origin isolated without requiring explicit
      # Cross-Origin-Resource-Policy on every asset (simpler than require-corp)
      header   = "Cross-Origin-Embedder-Policy"
      value    = "credentialless"
      override = true
    }
  }
}

locals {
  # Use an explicitly-provided certificate ARN first; fall back to the one
  # created in route53.tf when only a domain name is supplied.
  # try() prevents a plan error when the validation resource doesn't exist yet.
  effective_certificate_arn = var.certificate_arn != "" ? var.certificate_arn : (
    var.domain_name != "" ? try(aws_acm_certificate_validation.cloudfront[0].certificate_arn, "") : ""
  )
}

# CloudFront Distribution for Frontend
resource "aws_cloudfront_distribution" "frontend" {
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "CloudFront distribution for ${var.project_name} ${var.environment} frontend"
  default_root_object = "index.html"
  price_class         = var.environment == "prod" ? "PriceClass_All" : "PriceClass_100"
  aliases             = var.domain_name != "" ? [var.domain_name] : []

  # S3 Origin for static frontend
  origin {
    domain_name              = aws_s3_bucket.frontend.bucket_regional_domain_name
    origin_id                = "S3-${aws_s3_bucket.frontend.id}"
    origin_access_control_id = aws_cloudfront_origin_access_control.frontend.id
  }

  # ALB Origin for API
  origin {
    domain_name = aws_lb.api.dns_name
    origin_id   = "ALB-API"

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      # HTTP is used for CF→ALB because the ALB TLS certificate covers the
      # custom domain (capstone.robertb.me) but not the raw ELB hostname that
      # CloudFront uses for the origin. CloudFront validates the certificate
      # against the origin hostname, so https-only causes 502 errors.
      # The viewer-facing HTTPS (CF→browser) is unaffected; the app still sees
      # X-Forwarded-Proto:https from CloudFront and sets Secure on app cookies.
      # AWSALB stickiness cookies set by the ALB lack the Secure flag in HTTP
      # mode, but browsers still send them on HTTPS pages (SameSite=Lax default),
      # so SignalR sticky sessions continue to work.
      origin_protocol_policy = "http-only"
      origin_ssl_protocols   = ["TLSv1.2"]
      # CloudFront max origin_read_timeout without a quota increase is 60s.
      # Whisper cold starts can exceed this; the ECS API must handle the Lambda
      # invocation asynchronously if needed, but 60s covers warm invocations.
      origin_read_timeout      = 60
      origin_keepalive_timeout = 60
    }
  }

  # Lambda Function URL origin — used by the /wake behavior for instant ECS scale-up.
  # Only added when the ECS idle autoscaler is enabled (FURL resource has count conditional).
  dynamic "origin" {
    for_each = local.enable_ecs_idle_autoscaler_effective ? [aws_lambda_function_url.ecs_idle_scaler[0].function_url] : []

    content {
      # Strip "https://" and trailing "/" to get bare hostname for CF origin
      domain_name = trimsuffix(trimprefix(origin.value, "https://"), "/")
      origin_id   = "Lambda-Wake"

      custom_origin_config {
        http_port              = 80
        https_port             = 443
        origin_protocol_policy = "https-only"
        origin_ssl_protocols   = ["TLSv1.2"]
      }
    }
  }

  # Default cache behavior for frontend
  default_cache_behavior {
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "S3-${aws_s3_bucket.frontend.id}"

    forwarded_values {
      query_string = false

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy    = "redirect-to-https"
    min_ttl                   = 0
    default_ttl               = 3600
    max_ttl                   = 86400
    compress                  = true
    response_headers_policy_id = aws_cloudfront_response_headers_policy.coop_coep.id
  }

  # /wake behavior — direct Lambda invocation for instant ECS scale-up.
  # The browser WakeupGate POSTs here on the first non-200 /health response;
  # the Lambda immediately sets ECS desiredCount=1 without waiting for the
  # CloudWatch metric pipeline (~5 min delay for CF 5xxErrorRate to propagate).
  dynamic "ordered_cache_behavior" {
    for_each = local.enable_ecs_idle_autoscaler_effective ? [1] : []

    content {
      path_pattern     = "/wake"
      allowed_methods  = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
      cached_methods   = ["GET", "HEAD"]
      target_origin_id = "Lambda-Wake"

      forwarded_values {
        query_string = false

        cookies {
          forward = "none"
        }
      }

      viewer_protocol_policy = "redirect-to-https"
      min_ttl                = 0
      default_ttl            = 0
      max_ttl                = 0
      compress               = false
    }
  }

  # Static assets — served from S3 so the app is always available even when ECS is at 0 tasks.
  # index.html loads from S3, which contains the WakeupGate that polls /health to wake ECS.
  # Only /api/*, /health, /swagger*, and /hubs/* still route to the ALB.
  # NOTE: Do NOT forward the Host header to S3. Forwarding Host sends the CloudFront domain
  # (d3t5kcvyyonw8d.cloudfront.net) to S3, which breaks OAC SigV4 signing and returns 404.
  ordered_cache_behavior {
    path_pattern     = "/*.html"
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "S3-${aws_s3_bucket.frontend.id}"

    forwarded_values {
      query_string = false

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy     = "redirect-to-https"
    min_ttl                    = 0
    default_ttl                = 0
    max_ttl                    = 0
    compress                   = true
    response_headers_policy_id = aws_cloudfront_response_headers_policy.coop_coep.id
  }

  ordered_cache_behavior {
    path_pattern     = "/*.js"
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "S3-${aws_s3_bucket.frontend.id}"

    forwarded_values {
      query_string = false

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = true
    response_headers_policy_id = aws_cloudfront_response_headers_policy.coop_coep.id
  }

  ordered_cache_behavior {
    path_pattern     = "/*.css"
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "S3-${aws_s3_bucket.frontend.id}"

    forwarded_values {
      query_string = false

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = true
    response_headers_policy_id = aws_cloudfront_response_headers_policy.coop_coep.id
  }

  # ONNX Runtime / Silero VAD static assets served by ASP.NET Core wwwroot.
  # .mjs, .wasm, and .onnx files are NOT in S3 — they live in the container's wwwroot.
  # Without explicit behaviors these fall to the S3 default, which 403s → custom error
  # returns index.html (text/html) and the browser rejects them as wrong MIME type.
  ordered_cache_behavior {
    path_pattern     = "/*.mjs"
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "S3-${aws_s3_bucket.frontend.id}"

    forwarded_values {
      query_string = false

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = true
    response_headers_policy_id = aws_cloudfront_response_headers_policy.coop_coep.id
  }

  ordered_cache_behavior {
    path_pattern     = "/*.wasm"
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "S3-${aws_s3_bucket.frontend.id}"

    forwarded_values {
      query_string = false

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = false
    response_headers_policy_id = aws_cloudfront_response_headers_policy.coop_coep.id
  }

  ordered_cache_behavior {
    path_pattern     = "/*.onnx"
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "S3-${aws_s3_bucket.frontend.id}"

    forwarded_values {
      query_string = false

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = false
  }

  # File upload behavior — placed before /api/* so it takes priority.
  # File uploads are multipart/form-data binary POSTs; disabling CloudFront
  # response compression avoids any interference with binary response bodies
  # (e.g. JSON with embedded base64) and keeps the Content-Encoding header
  # clean for the browser.  All request headers are forwarded so the origin
  # receives the full multipart headers intact.
  ordered_cache_behavior {
    path_pattern     = "/api/admin/files*"
    allowed_methods  = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "ALB-API"

    forwarded_values {
      query_string = true
      headers      = ["Authorization", "Accept", "Content-Type", "Origin", "Content-Length", "Content-Disposition"]

      cookies {
        forward = "all"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = false
    response_headers_policy_id = aws_cloudfront_response_headers_policy.coop_coep.id

    function_association {
      event_type   = "viewer-response"
      function_arn = aws_cloudfront_function.secure_cookies.arn
    }
  }

  # Cache behavior for API
  ordered_cache_behavior {
    path_pattern     = "/api/*"
    allowed_methods  = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "ALB-API"

    forwarded_values {
      query_string = true
      headers      = ["Authorization", "Accept", "Content-Type", "Origin", "Host"]

      cookies {
        forward = "all"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = true
    response_headers_policy_id = aws_cloudfront_response_headers_policy.coop_coep.id

    function_association {
      event_type   = "viewer-response"
      function_arn = aws_cloudfront_function.secure_cookies.arn
    }
  }

  # Health, swagger, and other top-level ALB-served endpoints
  ordered_cache_behavior {
    path_pattern     = "/health"
    allowed_methods  = ["GET", "HEAD"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "ALB-API"

    forwarded_values {
      query_string = false
      headers      = ["Host"]

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = true
  }

  ordered_cache_behavior {
    path_pattern     = "/swagger*"
    allowed_methods  = ["GET", "HEAD", "OPTIONS"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "ALB-API"

    forwarded_values {
      query_string = true
      headers      = ["Host"]

      cookies {
        forward = "none"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = true
  }

  # Cache behavior for SignalR hub
  ordered_cache_behavior {
    path_pattern     = "/hubs/*"
    allowed_methods  = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods   = ["GET", "HEAD"]
    target_origin_id = "ALB-API"

    forwarded_values {
      query_string = true
      headers      = ["*"]

      cookies {
        forward = "all"
      }
    }

    viewer_protocol_policy = "redirect-to-https"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 0
    compress               = false

    function_association {
      event_type   = "viewer-response"
      function_arn = aws_cloudfront_function.secure_cookies.arn
    }
  }

  # Custom error response for SPA routing
  custom_error_response {
    error_code         = 404
    response_code      = 200
    response_page_path = "/index.html"
  }

  custom_error_response {
    error_code         = 403
    response_code      = 200
    response_page_path = "/index.html"
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = local.effective_certificate_arn == ""
    acm_certificate_arn            = local.effective_certificate_arn != "" ? local.effective_certificate_arn : null
    ssl_support_method             = local.effective_certificate_arn != "" ? "sni-only" : null
    minimum_protocol_version       = local.effective_certificate_arn != "" ? "TLSv1.2_2021" : "TLSv1"
  }

  tags = {
    Name = "${var.project_name}-${var.environment}-frontend-cf"
  }
}

# CloudFront Origin Request Policy for API
resource "aws_cloudfront_origin_request_policy" "api" {
  name    = "${var.project_name}-${var.environment}-api-policy"
  comment = "Policy for API requests"

  cookies_config {
    cookie_behavior = "all"
  }

  headers_config {
    header_behavior = "whitelist"
    headers {
      items = ["Accept", "Content-Type", "Origin", "User-Agent"]
    }
  }

  query_strings_config {
    query_string_behavior = "all"
  }
}

resource "aws_cloudfront_function" "secure_cookies" {
  name    = "${var.project_name}-${var.environment}-secure-cookies"
  runtime = "cloudfront-js-1.0"
  comment = "Adds the Secure flag to AWS ALB stickiness cookies that are generated over HTTP."
  publish = true
  code    = file("${path.module}/cloudfront-function.js")
}
